using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using SayAll.Core.Audio;
using SayAll.Core.Input;
using SayAll.Core.Onboarding;
using SayAll.HidBridge.Contracts;

namespace SayAll.Windows;

public partial class MainWindow : Window
{
    private const int MaxPendingSpectrumFrames = 192;
    private readonly DispatcherTimer _statusTimer;
    private readonly DispatcherTimer _gestureTimer;
    private readonly DispatcherTimer _spectrumTimer;
    private readonly OnboardingCapabilities _capabilities = new();
    private readonly Rc003AtvvClient _atvvClient = new();
    private readonly PcmAudioOutput _audioOutput = new();
    private readonly PcmCaptureBuffer _voiceCapture = new();
    private readonly LocalVoicePlayer _voicePlayer = new();
    private readonly VoiceToggleController _voiceToggle = new();
    private RemoteButtonGestureRecognizer _gestureRecognizer = new(
        doubleClickButtons: [RemoteButton.Tv]);
    private readonly RemoteMicrophonePressGate _microphonePressGate =
        new(TimeSpan.FromMilliseconds(250));
    private readonly RemoteActionExecutor _actionExecutor = new();
    private readonly SayAllSettingsStore _settingsStore = new();
    private readonly HashSet<RemoteButton> _testedButtons = [];
    private readonly string _bridgeLogPath = HidBridgeRuntimePaths.SharedEventLogPath;
    private FileSystemWatcher? _bridgeLogWatcher;
    private DefaultAudioCaptureRoute? _captureRoute;
    private WindowsVoiceInputProfile? _activeVoiceInputProfile;

    private bool _isRefreshing;
    private bool _bridgeStarting;
    private bool _hookReady;
    private int _processedLogLineCount;
    private string? _bridgeError;
    private string? _bridgeProgressDetail;
    private RemoteButton? _lastButton;
    private bool _audioInitializing;
    private bool _changingDevice;
    private CancellationTokenSource? _audioConnectionCancellation;
    private Task _audioInitializationCompletion = Task.CompletedTask;
    private bool _microphoneHidPressed;
    private bool _voiceInputTestPressed;
    private bool _loadingSettings;
    private string? _lastRecordingPath;
    private SayAllSettings _settings = new();
    private readonly RemoteBindingEditorSession _bindingEditor =
        new(RemoteBindingPresets.CreateCodex());
    private string? _selectedHotspotName;
    private OnboardingStep _currentStep = OnboardingStep.Remote;
    private DateTimeOffset _nextAudioReconnectAt;
    private readonly ConcurrentQueue<VoiceSpectrumFrame> _pendingSpectrumFrames = new();
    private bool _spectrumHasRenderedCurrentSession;
    private int _receivedSpectrumFrameCount;
    private int _renderedSpectrumFrameCount;
    private bool IsMicrophoneSessionHeld =>
        _microphoneHidPressed || _voiceInputTestPressed;

    private readonly bool _preview;
    public MainWindow(bool preview = false)
    {
        _preview = preview;
        InitializeComponent();
        _statusTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1),
        };
        _statusTimer.Tick += StatusTimer_Tick;
        _gestureTimer = new DispatcherTimer(DispatcherPriority.Input)
        {
            Interval = InputDispatchPolicy.GestureFlushInterval,
        };
        _gestureTimer.Tick += GestureTimer_Tick;
        _spectrumTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(33),
        };
        _spectrumTimer.Tick += SpectrumTimer_Tick;
        _atvvClient.StatusChanged += AtvvClient_StatusChanged;
        _atvvClient.CapabilitiesReceived += AtvvClient_CapabilitiesReceived;
        _atvvClient.RemoteMicrophonePressed += AtvvClient_RemoteMicrophonePressed;
        _atvvClient.VoiceStarted += AtvvClient_VoiceStarted;
        _atvvClient.PcmFrameReady += AtvvClient_PcmFrameReady;
        _atvvClient.VoiceStopped += AtvvClient_VoiceStopped;
        _atvvClient.Failed += AtvvClient_Failed;
        Loaded += OnLoaded;
        Closed += OnClosed;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_preview)
        {
            LoadDeviceProfiles();
            ApplyModelBindings();
            ShowOnboardingStep(OnboardingStep.Controls);
            HistoryStatusText.Text = "界面预览 · 未连接硬件";
            return;
        }
        InitializeHistory();
        LoadDeviceProfiles();
        await LoadSettingsAsync();
        ApplyModelBindings();
        ProvisionCodexReasoningShortcut();
        InitializeBridgeLogCursor();
        StartBridgeLogWatcher();
        _statusTimer.Start();
        _gestureTimer.Start();
        await RefreshStatusAsync();
    }

    private void ProvisionCodexReasoningShortcut()
    {
        try
        {
            if (CodexKeybindingProvisioner.EnsureReasoningShortcut())
            {
                ControlsDetail.Text = "已准备 Codex 活动视图和推理选择快捷键";
            }
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            InvalidDataException or
            InvalidOperationException)
        {
            ControlsDetail.Text = $"Codex 快捷键配置失败：{exception.Message}";
        }
    }

    private void InitializeBridgeLogCursor()
    {
        if (!File.Exists(_bridgeLogPath))
        {
            return;
        }

        _processedLogLineCount = 0;
        ReadBridgeLog(applyReports: false);
    }

    private async void OnClosed(object? sender, EventArgs e)
    {
        if (_preview) return;
        _statusTimer.Stop();
        _gestureTimer.Stop();
        _spectrumTimer.Stop();
        ClearPendingSpectrumFrames();
        _bridgeLogWatcher?.Dispose();
        _changingDevice = true;
        await CancelAudioInitializationAsync();
        FinishUsageSession();
        _reflectionCapture?.Dispose();
        StopVoiceInputSession();
        await _atvvClient.DisposeAsync();
        _audioOutput.Dispose();
        _voicePlayer.Dispose();
        await FlushJournalAsync();
        Application.Current.Shutdown();
    }

    private async void StatusTimer_Tick(object? sender, EventArgs e)
    {
        await RefreshStatusAsync();
    }

    private void GestureTimer_Tick(object? sender, EventArgs e)
    {
        ExecuteGestures(_gestureRecognizer.Flush(DateTimeOffset.Now));
    }

    private void SpectrumTimer_Tick(object? sender, EventArgs e)
    {
        RenderLatestSpectrumFrame();
    }

    private void RenderLatestSpectrumFrame()
    {
        var spectrumFrames = new List<VoiceSpectrumFrame>(MaxPendingSpectrumFrames);
        while (_pendingSpectrumFrames.TryDequeue(out var spectrumFrame))
        {
            spectrumFrames.Add(spectrumFrame);
        }

        if (spectrumFrames.Count == 0)
        {
            return;
        }

        if (!_spectrumHasRenderedCurrentSession)
        {
            HomeVoiceSpectrum.SetRecording(true);
            _spectrumHasRenderedCurrentSession = true;
        }

        HomeVoiceSpectrum.PushFrames(spectrumFrames);
        _renderedSpectrumFrameCount += spectrumFrames.Count;
        var latestSpectrumFrame = spectrumFrames[^1];
        HomeSpectrumPeak.Text = $"{FormatDb(latestSpectrumFrame.PeakDbFs)} dB";
        HomeSpectrumStatus.Text =
            $"正在接收真实声音 · 收到 {Volatile.Read(ref _receivedSpectrumFrameCount)} 帧 · " +
            $"已显示 {_renderedSpectrumFrameCount} 帧";
        var duration = _voiceCapture.DurationSeconds(16_000d);
        var peak = _voiceCapture.PeakDbFs;
        VoiceTestDetail.Text =
            $"正在采集 {duration:0.0} 秒 · 峰值 {FormatDb(peak)} dBFS";
        VoiceLevelMeter.Value = ToMeterValue(peak);
        VoiceStateDot.Background = FindBrush("SuccessBrush");
    }

    private void StartBridgeLogWatcher()
    {
        var directory = Path.GetDirectoryName(_bridgeLogPath);
        if (directory is null || !Directory.Exists(directory))
        {
            return;
        }

        _bridgeLogWatcher = new FileSystemWatcher(
            directory,
            Path.GetFileName(_bridgeLogPath))
        {
            NotifyFilter = NotifyFilters.FileName |
                NotifyFilters.LastWrite |
                NotifyFilters.Size,
        };
        _bridgeLogWatcher.Changed += BridgeLogWatcher_Changed;
        _bridgeLogWatcher.Created += BridgeLogWatcher_Changed;
        _bridgeLogWatcher.Renamed += BridgeLogWatcher_Changed;
        _bridgeLogWatcher.EnableRaisingEvents = true;
    }

    private void BridgeLogWatcher_Changed(object sender, FileSystemEventArgs e)
    {
        if (Dispatcher.HasShutdownStarted)
        {
            return;
        }

        _ = Dispatcher.BeginInvoke(
            DispatcherPriority.Input,
            new Action(() => ReadBridgeLog()));
    }

    private async void RefreshConnection_Click(object sender, RoutedEventArgs e)
    {
        await RefreshStatusAsync();
    }

    private async void ContinueButton_Click(object sender, RoutedEventArgs e)
    {
        ShowOnboardingStep(OnboardingStep.Audio);
        await InitializeAudioAsync();
    }

    private async void RetryAudio_Click(object sender, RoutedEventArgs e)
    {
        await InitializeAudioAsync();
    }

    private void AudioContinue_Click(object sender, RoutedEventArgs e)
    {
        if (!OnboardingFlowPolicy.CanContinue(OnboardingStep.Audio, _capabilities))
        {
            return;
        }

        ShowOnboardingStep(OnboardingStep.VoiceTest);
        var duration = _voiceCapture.DurationSeconds(16_000d);
        VoiceCaptureSummary.Text =
            $"已保存 {duration:0.0} 秒录音 · 峰值 {FormatDb(_voiceCapture.PeakDbFs)} dBFS";
        VoicePlaybackButton.IsEnabled = _lastRecordingPath is not null;
        VoicePlaybackStatus.Text = "点击试听，确认录到的是你刚才说的话";
        ConfirmVoiceButton.IsEnabled = false;
        _capabilities.VoicePlaybackOpened = false;
        _capabilities.VoicePlaybackConfirmed = false;
    }

    private void PlayVoiceSample_Click(object sender, RoutedEventArgs e)
    {
        if (_lastRecordingPath is null || !File.Exists(_lastRecordingPath))
        {
            VoicePlaybackStatus.Text = "试听文件不存在，请返回重新录制";
            ConfirmVoiceButton.IsEnabled = false;
            return;
        }

        try
        {
            _voicePlayer.Load(_lastRecordingPath);
            _voicePlayer.Play();
            _capabilities.VoicePlaybackOpened = true;
            VoicePlaybackStatus.Text = "正在 VoiceAnything 内播放本地录音；听完后确认声音是否正确";
            ConfirmVoiceButton.IsEnabled = true;
        }
        catch (Exception exception) when (
            exception is IOException or InvalidOperationException or NotSupportedException)
        {
            VoicePlaybackStatus.Text = $"无法在 VoiceAnything 内播放录音：{exception.Message}";
            ConfirmVoiceButton.IsEnabled = false;
        }
    }

    private void ConfirmVoice_Click(object sender, RoutedEventArgs e)
    {
        _capabilities.VoicePlaybackConfirmed = true;
        if (!OnboardingFlowPolicy.CanContinue(OnboardingStep.VoiceTest, _capabilities))
        {
            return;
        }

        ShowOnboardingStep(OnboardingStep.Controls);
        _testedButtons.Clear();
        _capabilities.TestedRemoteButtonCount = 0;
        ControlsDetail.Text = "请依次按下三个不同按键";
        ControlsProgress.Value = 0;
    }

    private async void Finish_Click(object sender, RoutedEventArgs e)
    {
        if (!OnboardingFlowPolicy.CanContinue(OnboardingStep.Controls, _capabilities))
        {
            return;
        }

        ControlsDetail.Text = _capabilities.TestedRemoteButtonCount >= 3
            ? "配置已保存并启用 · 已完成三个真机按键验证"
            : "配置已保存并启用 · 真机按键验证可稍后继续";
        FinishButton.Content = "已保存并启用";
        FinishButton.IsEnabled = false;
        _settings.RemoteActionsEnabled = true;
        await SaveSettingsAsync();
    }

    private async void OnboardingStep_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button button ||
            button.Tag is not string stepName ||
            !Enum.TryParse<OnboardingStep>(stepName, out var step))
        {
            return;
        }

        ShowOnboardingStep(step);
        if (step == OnboardingStep.Audio)
        {
            await InitializeAudioAsync();
        }
    }

    private void ShowOnboardingStep(OnboardingStep step)
    {
        _currentStep = step;
        RemoteStep.Visibility = step == OnboardingStep.Remote
            ? Visibility.Visible
            : Visibility.Collapsed;
        AudioStep.Visibility = step == OnboardingStep.Audio
            ? Visibility.Visible
            : Visibility.Collapsed;
        VoiceStep.Visibility = step == OnboardingStep.VoiceTest
            ? Visibility.Visible
            : Visibility.Collapsed;
        ControlsStep.Visibility = step == OnboardingStep.Controls
            ? Visibility.Visible
            : Visibility.Collapsed;
        OnboardingProgress.Width = step switch
        {
            OnboardingStep.Remote => 250,
            OnboardingStep.Audio => 500,
            OnboardingStep.VoiceTest => 750,
            OnboardingStep.Controls => 1000,
            _ => 250,
        };

        var stepButtons = new Dictionary<System.Windows.Controls.Button, OnboardingStep>
        {
            [RemoteStepButton] = OnboardingStep.Remote,
            [AudioStepButton] = OnboardingStep.Audio,
            [VoiceStepButton] = OnboardingStep.VoiceTest,
            [ControlsStepButton] = OnboardingStep.Controls,
        };
        foreach (var item in stepButtons)
        {
            var active = item.Value == step;
            item.Key.FontWeight = active ? FontWeights.SemiBold : FontWeights.Normal;
            item.Key.Foreground = active
                ? FindBrush("TextBrush")
                : FindBrush("SecondaryTextBrush");
        }

        if (step == OnboardingStep.Controls)
        {
            FinishButton.Content = "保存并启用";
            FinishButton.IsEnabled = true;
        }
    }

    private async Task InitializeAudioAsync()
    {
        if (_changingDevice) return;
        if (_selectedProfile?.Profile.Capabilities.Voice != true)
        { AudioStatusText.Text = "请先选择支持语音的型号。"; return; }
        if (_audioInitializing)
        {
            return;
        }

        _audioInitializing = true;
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _audioInitializationCompletion = completion.Task;
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        _audioConnectionCancellation = cancellation;
        AudioRetryButton.IsEnabled = false;
        AudioStatusText.Text = "正在连接本机录音设备…";
        VoiceTestDetail.Text = "连接完成后，按住麦克风键说话";
        try
        {
            _capabilities.AudioOutputSelected = false;
            _capabilities.AudioReady = false;
            _capabilities.VoiceSessionStarted = false;
            _capabilities.VoiceSamplesReceived = false;
            _capabilities.VoiceSessionEnded = false;
            _voiceCapture.Reset();
            _lastRecordingPath = null;
            AudioPlaybackButton.IsEnabled = false;
            AudioContinueButton.IsEnabled = false;
            _voiceToggle.Reset();
            StopVoiceInputSession();
            _voicePlayer.Stop();
            VoiceLevelMeter.Value = 0;
            await _audioOutput.InitializeCableAsync();
            _capabilities.AudioOutputSelected = true;
            AudioOutputDetail.Text = $"已选择：{_audioOutput.DeviceName}";
            AudioOutputDot.Background = FindBrush("SuccessBrush");
            cancellation.Token.ThrowIfCancellationRequested();
            await _atvvClient.ConnectAsync(cancellation.Token);
        }
        catch (OperationCanceledException)
        {
            AudioStatusText.Text = _changingDevice ? "正在切换设备…" : "连接超时，请唤醒遥控器后重试。";
            AudioRetryButton.IsEnabled = true;
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or UnauthorizedAccessException or COMException or IOException or System.Text.Json.JsonException)
        {
            AudioStatusText.Text = $"连接失败：{exception.Message}";
            AudioStatusDot.Background = FindBrush("PendingBrush");
            AudioRetryButton.IsEnabled = true;
        }
        finally
        {
            _audioInitializing = false;
            _audioConnectionCancellation = null;
            completion.TrySetResult();
        }
    }

    private async Task CancelAudioInitializationAsync()
    {
        _audioConnectionCancellation?.Cancel();
        await _audioInitializationCompletion;
    }

    private void AtvvClient_StatusChanged(string status)
    {
        Dispatcher.Invoke(() => AudioStatusText.Text = status);
    }

    private void AtvvClient_CapabilitiesReceived(SayAll.Core.Audio.AtvvCapabilities capabilities)
    {
        Dispatcher.Invoke(() =>
        {
            _capabilities.AudioReady = SayAll.Core.Audio.AtvvProtocol.SupportsAudio(
                capabilities.SampleRate);
            AudioStatusText.Text =
                $"语音连接已就绪 · {capabilities.SampleRate / 1000:0} kHz · 帧长 {capabilities.FrameSize}";
            AudioStatusDot.Background = FindBrush("SuccessBrush");
            VoiceTestDetail.Text = "按住麦克风键说话，松开后结束";
            AudioRetryButton.IsEnabled = false;
        });
    }

    private void AtvvClient_RemoteMicrophonePressed()
    {
        if (_hookReady)
        {
            return;
        }

        HandleRemoteMicrophonePressed(DateTimeOffset.Now);
    }

    private void HandleRemoteMicrophonePressed(DateTimeOffset timestamp)
    {
        if (!_microphonePressGate.TryAccept(timestamp))
        {
            return;
        }

        Dispatcher.BeginInvoke(async () =>
        {
            try
            {
                var selectedInputMethod = GetSelectedVoiceInputProfile();
                var action = _voiceToggle.OnMicrophoneButtonPressed(
                    selectedInputMethod is not null);
                switch (action)
                {
                    case VoiceToggleAction.RequestInputMethodSelection:
                        InputMethodStatus.Text = "请先在这里选择录音时使用的输入法";
                        InputMethodListBox.Focus();
                        VoiceTestDetail.Text = "已收到麦克风按键；选择输入法后再按住说话";
                        break;
                    case VoiceToggleAction.OpenMicrophone:
                        var routeWarning = StartVoiceInputSession(selectedInputMethod!);
                        InputMethodStatus.Text = routeWarning is null
                            ? WindowsVoiceInputInteractionPolicy.GetRecordingHint(
                                selectedInputMethod!.Kind)
                            : $"{selectedInputMethod!.DisplayName} 启动失败：{routeWarning}";
                        VoiceTestDetail.Text = "正在请求 RC003MS 开始录音…";
                        try
                        {
                            await _atvvClient.RequestMicrophoneOpenAsync();
                        }
                        catch (Exception exception) when (
                            exception is InvalidOperationException or COMException or UnauthorizedAccessException)
                        {
                            _voiceToggle.Reset();
                            StopVoiceInputSession();
                            AtvvClient_Failed(exception);
                        }
                        break;
                    case VoiceToggleAction.CloseMicrophone:
                        StopVoiceInputSession();
                        _voiceToggle.NotifyVoiceStopped();
                        VoiceTestDetail.Text = "正在结束本次录音…";
                        try
                        {
                            await _atvvClient.RequestMicrophoneCloseAsync();
                        }
                        catch (Exception exception) when (
                            exception is InvalidOperationException or COMException or UnauthorizedAccessException)
                        {
                            AtvvClient_Failed(exception);
                        }
                        break;
                }
            }
            catch (Exception exception) when (
                exception is InvalidOperationException or COMException or UnauthorizedAccessException)
            {
                _voiceToggle.Reset();
                StopVoiceInputSession();
                AtvvClient_Failed(exception);
            }
        });
    }

    private void HandleRemoteMicrophoneReleased(DateTimeOffset timestamp)
    {
        Dispatcher.BeginInvoke(async () =>
        {
            if (_activeVoiceInputProfile is null &&
                _voiceToggle.State == VoiceToggleState.Idle)
            {
                return;
            }

            var stoppedInputMethod = _activeVoiceInputProfile ??
                GetSelectedVoiceInputProfile();
            StopVoiceInputSession();
            _voiceToggle.NotifyVoiceStopped();
            if (stoppedInputMethod is not null)
            {
                InputMethodStatus.Text =
                    WindowsVoiceInputInteractionPolicy.GetStoppedHint(
                        stoppedInputMethod.Kind);
            }
            VoiceTestDetail.Text = $"实体录音键已松开 · {timestamp:HH:mm:ss.fff}";
            try
            {
                await _atvvClient.RequestMicrophoneCloseAsync();
            }
            catch (Exception exception) when (
                exception is InvalidOperationException or COMException or UnauthorizedAccessException)
            {
                AtvvClient_Failed(exception);
            }
        });
    }

    private void AtvvClient_VoiceStarted()
    {
        Dispatcher.Invoke(() =>
        {
            if (_hookReady && !IsMicrophoneSessionHeld &&
                _activeVoiceInputProfile is null)
            {
                _ = CloseUnexpectedRemoteStreamAsync();
                return;
            }

            EnsureVoiceInputSessionStartedFromStream();
            var continuingHeldSession =
                IsMicrophoneSessionHeld &&
                _voiceToggle.State == VoiceToggleState.Recording &&
                _capabilities.VoiceSessionStarted;
            if (!continuingHeldSession)
            {
                FinishUsageSession();
                _statisticsVoiceStart = DateTimeOffset.Now;
                ClearPendingSpectrumFrames();
                _spectrumHasRenderedCurrentSession = false;
                Interlocked.Exchange(ref _receivedSpectrumFrameCount, 0);
                _renderedSpectrumFrameCount = 0;
                _voicePlayer.Stop();
                _voiceCapture.Reset();
                _lastRecordingPath = null;
                AudioPlaybackButton.IsEnabled = false;
                AudioContinueButton.IsEnabled = false;
            }

            _spectrumTimer.Start();
            HomeSpectrumLiveDot.Fill = new SolidColorBrush(Color.FromRgb(255, 91, 34));
            HomeSpectrumStatus.Text = continuingHeldSession
                ? "录音键仍按住 · RC003MS 音频分段已续接"
                : "录音键已按下 · 正在等待第一帧真实声音";
            _voiceToggle.NotifyVoiceStarted();
            _capabilities.VoiceSessionStarted = true;
            _capabilities.VoiceSamplesReceived = false;
            _capabilities.VoiceSessionEnded = false;
            VoiceTestDetail.Text = "正在录音；请保持按住麦克风键，松开后结束";
            VoiceStateDot.Background = FindBrush("AccentBrush");
        });
    }

    private async Task CloseUnexpectedRemoteStreamAsync()
    {
        try
        {
            await _atvvClient.RequestMicrophoneCloseAsync();
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or COMException or UnauthorizedAccessException)
        {
            AtvvClient_Failed(exception);
        }
    }

    private void EnsureVoiceInputSessionStartedFromStream()
    {
        var selectedInputMethod = GetSelectedVoiceInputProfile();
        if (_activeVoiceInputProfile is not null || selectedInputMethod is null)
        {
            return;
        }

        var routeWarning = StartVoiceInputSession(selectedInputMethod);
        InputMethodStatus.Text = routeWarning is null
            ? WindowsVoiceInputInteractionPolicy.GetRecordingHint(
                selectedInputMethod.Kind)
            : $"{selectedInputMethod.DisplayName} 启动失败：{routeWarning}";
    }

    private void AtvvClient_PcmFrameReady(short[] samples)
    {
        try
        {
            _audioOutput.Write(samples);
            _voiceCapture.Append(samples);
            _capabilities.VoiceSamplesReceived = _voiceCapture.SampleCount > 0;
            var spectrumFrame = VoiceSpectrumAnalyzer.Analyze(samples);
            _pendingSpectrumFrames.Enqueue(spectrumFrame);
            while (_pendingSpectrumFrames.Count > MaxPendingSpectrumFrames)
            {
                _pendingSpectrumFrames.TryDequeue(out _);
            }
            Interlocked.Increment(ref _receivedSpectrumFrameCount);
        }
        catch (Exception exception)
        {
            AtvvClient_Failed(exception);
        }
    }

    private void AtvvClient_VoiceStopped()
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(AtvvClient_VoiceStopped); return; }
        if (IsMicrophoneSessionHeld)
        {
            Dispatcher.BeginInvoke(async () =>
            {
                VoiceTestDetail.Text = "录音键仍按住；正在续接 RC003MS 音频分段…";
                try
                {
                    await _atvvClient.RequestMicrophoneOpenAsync();
                }
                catch (Exception exception) when (
                    exception is InvalidOperationException or COMException or UnauthorizedAccessException)
                {
                    AtvvClient_Failed(exception);
                }
            });
            return;
        }

        try
        {
            StopVoiceInputSession();
            _voiceToggle.NotifyVoiceStopped();
            _capabilities.VoiceSessionEnded = true;
            if (_voiceCapture.SampleCount > 0)
            {
                FinishUsageSession();
                if (IsRecordingTest) _lastRecordingPath = SaveCapturedVoice();
            }

            Dispatcher.Invoke(() =>
            {
                _spectrumTimer.Stop();
                RenderLatestSpectrumFrame();
                if (_spectrumHasRenderedCurrentSession)
                {
                    HomeVoiceSpectrum.SetRecording(false);
                }
                HomeSpectrumLiveDot.Fill = new SolidColorBrush(Color.FromRgb(111, 74, 69));
                var duration = _voiceCapture.DurationSeconds(16_000d);
                var hasSamples = _voiceCapture.SampleCount > 0;
                HomeSpectrumStatus.Text = _spectrumHasRenderedCurrentSession
                    ? $"录音已结束 · 收到 {Volatile.Read(ref _receivedSpectrumFrameCount)} 帧 · " +
                      $"已显示 {_renderedSpectrumFrameCount} 帧"
                    : "本次按键过短，未收到音频 · 已保留上一屏真实声谱";
                var signalHint = _voiceCapture.HasAudibleSignal
                    ? "检测到明显声音"
                    : "音量较低，请试听确认";
                VoiceTestDetail.Text = hasSamples
                    ? $"已采集 {duration:0.0} 秒 · {signalHint} · 峰值 {FormatDb(_voiceCapture.PeakDbFs)} dBFS"
                    : "语音已结束，但尚未收到音频样本，请重试";
                VoiceStateDot.Background = hasSamples
                    ? FindBrush("SuccessBrush")
                    : FindBrush("PendingBrush");
                AudioPlaybackButton.IsEnabled = _lastRecordingPath is not null;
                AudioContinueButton.IsEnabled = OnboardingFlowPolicy.CanContinue(
                    OnboardingStep.Audio,
                    _capabilities);
            });
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            AtvvClient_Failed(exception);
        }
    }

    private void AtvvClient_Failed(Exception exception)
    {
        Dispatcher.Invoke(() =>
        {
            AudioStatusText.Text = $"语音连接出错：{exception.Message}";
            AudioStatusDot.Background = FindBrush("PendingBrush");
            AudioRetryButton.IsEnabled = true;
            VoiceTestDetail.Text = "远程音频未连接；松开录音键后请重试";
        });
    }

    private void ClearPendingSpectrumFrames()
    {
        while (_pendingSpectrumFrames.TryDequeue(out _))
        {
        }
    }

    private async Task LoadSettingsAsync()
    {
        _loadingSettings = true;
        try
        {
            try
            {
                _settings = await _settingsStore.LoadAsync();
            }
            catch (InvalidDataException exception)
            {
                _settings = new SayAllSettings();
                InputMethodStatus.Text = $"设置未载入：{exception.Message}";
            }

            var normalizedInputMethodId = WindowsVoiceInputCatalog.NormalizePersistedId(
                _settings.PreferredInputMethodId);
            var migratedVoiceInput = !string.Equals(
                normalizedInputMethodId,
                _settings.PreferredInputMethodId,
                StringComparison.OrdinalIgnoreCase);
            _settings.PreferredInputMethodId = normalizedInputMethodId;
            HomeVoiceSpectrum.SetPalette(_settings.SpectrumPalette);
            SetSpectrumPaletteSelection(_settings.SpectrumPalette);

            var bindingProfile = _settings.RemoteBindings.Count > 0
                ? new RemoteBindingProfile(_settings.ActivePreset, _settings.RemoteBindings)
                : GetPreset(_settings.ActivePreset);
            _bindingEditor.ApplyProfile(bindingProfile);
            ActivePresetText.Text = $"当前起步模板：{bindingProfile.Name}";

            var inputMethods = WindowsVoiceInputCatalog.GetAvailableProfiles()
                .Select(CreateVoiceInputTestItem)
                .ToArray();
            InputMethodListBox.ItemsSource = inputMethods;
            InputMethodListBox.SelectedItem = inputMethods.FirstOrDefault(item =>
                item.Profile.Id.Equals(
                    _settings.PreferredInputMethodId,
                    StringComparison.OrdinalIgnoreCase));
            if (InputMethodListBox.SelectedItem is VoiceInputTestItem selected)
            {
                InputMethodStatus.Text =
                    $"已记住：{selected.Profile.DisplayName}；" +
                    WindowsVoiceInputInteractionPolicy.GetHint(selected.Profile.Kind);
            }
            else if (inputMethods.Length == 0)
            {
                InputMethodStatus.Text = "Windows 当前用户没有可用输入法";
            }

            if (migratedVoiceInput)
            {
                await _settingsStore.SaveAsync(_settings);
            }
        }
        finally
        {
            _loadingSettings = false;
        }
    }

    private void SetSpectrumPaletteSelection(SpectrumPalette palette)
    {
        SpectrumPaletteBlue.IsChecked = palette == SpectrumPalette.Blue;
        SpectrumPaletteEmber.IsChecked = palette == SpectrumPalette.Ember;
        SpectrumPaletteEmerald.IsChecked = palette == SpectrumPalette.Emerald;
    }

    private async void SpectrumPalette_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.RadioButton radioButton ||
            radioButton.Tag is not string paletteName ||
            !Enum.TryParse<SpectrumPalette>(paletteName, out var selectedPalette))
        {
            return;
        }

        HomeVoiceSpectrum.SetPalette(selectedPalette);
        if (_loadingSettings)
        {
            return;
        }

        _settings.SpectrumPalette = selectedPalette;
        await SaveSettingsAsync();
    }

    private async void InputMethod_SelectionChanged(
        object sender,
        System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_loadingSettings ||
            InputMethodListBox.SelectedItem is not VoiceInputTestItem selectedItem)
        {
            return;
        }

        var selected = selectedItem.Profile;
        _settings.PreferredInputMethodId = selected.Id;
        InputMethodStatus.Text =
            $"已选择：{selected.DisplayName}；" +
            WindowsVoiceInputInteractionPolicy.GetHint(selected.Kind);
        await SaveSettingsAsync();
        if (!_atvvClient.IsConnected)
        {
            await InitializeAudioAsync();
        }
    }

    private void RefreshVoiceInputs_Click(object sender, RoutedEventArgs e)
    {
        RefreshVoiceInputItems();
        InputMethodStatus.Text = "输入法状态已刷新";
    }

    private void RefreshVoiceInputItems(string? selectedId = null)
    {
        selectedId ??= GetSelectedVoiceInputProfile()?.Id ?? _settings.PreferredInputMethodId;
        var items = WindowsVoiceInputCatalog.GetAvailableProfiles()
            .Select(CreateVoiceInputTestItem)
            .ToArray();
        _loadingSettings = true;
        try
        {
            InputMethodListBox.ItemsSource = items;
            InputMethodListBox.SelectedItem = items.FirstOrDefault(item =>
                item.Profile.Id.Equals(selectedId, StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            _loadingSettings = false;
        }
    }

    private static VoiceInputTestItem CreateVoiceInputTestItem(
        WindowsVoiceInputProfile profile)
    {
        var executableInstalled = profile.Kind switch
        {
            WindowsVoiceInputKind.CodexDictation => true,
            WindowsVoiceInputKind.Typeless =>
                File.Exists(WindowsVoiceInputCatalog.GetTypelessExecutablePath()),
            WindowsVoiceInputKind.WeChatVoice =>
                File.Exists(WindowsVoiceInputCatalog.GetWeChatExecutablePath()),
            WindowsVoiceInputKind.BageShuo =>
                File.Exists(WindowsVoiceInputCatalog.GetBageShuoExecutablePath()),
            _ => false,
        };
        var appRunning = profile.Kind switch
        {
            WindowsVoiceInputKind.CodexDictation => IsProcessRunning("ChatGPT"),
            WindowsVoiceInputKind.Typeless => IsProcessRunning("Typeless"),
            WindowsVoiceInputKind.WeChatVoice => IsProcessRunning("Weixin"),
            WindowsVoiceInputKind.BageShuo => IsProcessRunning("bageshuo"),
            _ => false,
        };
        var helperRunning = profile.Kind == WindowsVoiceInputKind.BageShuo &&
            IsProcessRunning("hotkey-listener");
        return new VoiceInputTestItem(
            profile,
            VoiceInputReadinessPolicy.Evaluate(
                profile.Kind,
                executableInstalled,
                appRunning,
                helperRunning));
    }

    private static bool IsProcessRunning(string processName)
    {
        try
        {
            var processes = Process.GetProcessesByName(processName);
            foreach (var process in processes)
            {
                process.Dispose();
            }

            return processes.Length > 0;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private WindowsVoiceInputProfile? GetSelectedVoiceInputProfile()
    {
        return (InputMethodListBox.SelectedItem as VoiceInputTestItem)?.Profile;
    }

    private void VoiceInputTest_PreviewMouseLeftButtonDown(
        object sender,
        System.Windows.Input.MouseButtonEventArgs e)
    {
        if (_voiceInputTestPressed ||
            sender is not System.Windows.Controls.Button button ||
            button.Tag is not WindowsVoiceInputProfile profile)
        {
            return;
        }

        InputMethodListBox.SelectedItem = InputMethodListBox.Items
            .OfType<VoiceInputTestItem>()
            .FirstOrDefault(item => item.Profile.Id.Equals(
                profile.Id,
                StringComparison.OrdinalIgnoreCase));
        button.CaptureMouse();
        button.Content = "松开结束";
        _voiceInputTestPressed = true;
        VoiceInputTestTextBox.Focus();
        InputMethodStatus.Text = $"正在测试：{profile.DisplayName}";
        HandleRemoteMicrophonePressed(DateTimeOffset.Now);
        e.Handled = true;
    }

    private void VoiceInputTest_PreviewMouseLeftButtonUp(
        object sender,
        System.Windows.Input.MouseButtonEventArgs e)
    {
        if (!_voiceInputTestPressed ||
            sender is not System.Windows.Controls.Button button ||
            button.Tag is not WindowsVoiceInputProfile profile)
        {
            return;
        }

        _voiceInputTestPressed = false;
        button.Content = "按住测试";
        button.ReleaseMouseCapture();
        HandleRemoteMicrophoneReleased(DateTimeOffset.Now);
        Dispatcher.BeginInvoke(
            DispatcherPriority.Background,
            () => RefreshVoiceInputItems(profile.Id));
        e.Handled = true;
    }

    private void RemoteHotspot_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button button ||
            button.Tag is not string buttonName)
        {
            return;
        }

        if (buttonName == "Microphone")
        {
            _selectedHotspotName = buttonName;
            SelectedRemoteButtonTitle.Text = "麦克风键";
            SelectedBindingDetail.Text =
                "固定为：按住时录音，松开后结束；输入法在录音设置中选择";
            BindingActionPanel.IsEnabled = false;
            BindingArgumentCard.Visibility = Visibility.Collapsed;
            UpdateHotspotSelection();
            return;
        }

        if (RemoteButton.TryParse(buttonName, out var remoteButton))
        {
            SelectRemoteButton(remoteButton, "平面图");
        }
    }

    private async void Preset_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button button ||
            button.Tag is not string presetName)
        {
            return;
        }

        var profile = GetPreset(presetName);
        _bindingEditor.ApplyProfile(profile);
        _settings.ActivePreset = profile.Name;
        ActivePresetText.Text = $"当前起步模板：{profile.Name}";
        if (_selectedHotspotName != "Microphone")
        {
            UpdateBindingSelection();
        }
        await SaveSettingsAsync();
        ControlsDetail.Text = $"已应用 {profile.Name} 起步模板；每个键仍可单独修改";
    }

    private void Trigger_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button button &&
            button.Tag is string triggerName &&
            Enum.TryParse<RemoteButtonTrigger>(triggerName, out var trigger))
        {
            _bindingEditor.SelectTrigger(trigger);
            UpdateBindingSelection();
        }
    }

    private async void BindingAction_Click(object sender, RoutedEventArgs e)
    {
        if (_bindingEditor.SelectedButton is null ||
            sender is not System.Windows.Controls.Button button ||
            button.Tag is not string actionName ||
            !Enum.TryParse<RemoteButtonAction>(actionName, out var action))
        {
            return;
        }

        var argument = action switch
        {
            RemoteButtonAction.InsertSkill when _bindingEditor.Profile.Name == "Claude Code" => "/",
            RemoteButtonAction.InsertSkill => "$",
            _ => null,
        };
        _bindingEditor.Assign(new RemoteButtonBinding(action, argument));
        _settings.ActivePreset = _bindingEditor.Profile.Name;
        UpdateBindingSelection();
        await SaveSettingsAsync();
    }

    private async void BindingArgumentSave_Click(object sender, RoutedEventArgs e)
    {
        if (_bindingEditor.SelectedButton is null)
        {
            return;
        }

        var current = _bindingEditor.CurrentBinding;
        if (current.Action != RemoteButtonAction.InsertSkill)
        {
            return;
        }

        var argument = BindingArgumentTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(argument))
        {
            BindingArgumentHint.Text = "请输入要插入的 Skill，例如 $baseline-packager";
            return;
        }

        _bindingEditor.Assign(new RemoteButtonBinding(current.Action, argument));
        await SaveSettingsAsync();
        UpdateBindingSelection();
        ControlsDetail.Text = $"已保存：{GetButtonName(_bindingEditor.SelectedButton.Value)} · {GetTriggerName(_bindingEditor.SelectedTrigger)}";
    }

    private async Task SaveSettingsAsync()
    {
        try
        {
            if (_selectedProfile is not null)
                _settings.ModelBindings[_selectedProfile.Profile.Id] = new(_bindingEditor.Profile.Name, _bindingEditor.Profile.Bindings.ToList());
            if (_selectedProfile?.Profile.Id == "xiaomi-rc003")
                _settings.RemoteBindings = _bindingEditor.Profile.Bindings.ToList();
            await _settingsStore.SaveAsync(_settings);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            ControlsDetail.Text = $"设置保存失败：{exception.Message}";
        }
    }

    private void UpdateBindingSelection()
    {
        if (_bindingEditor.SelectedButton is not RemoteButton selectedButton)
        {
            return;
        }

        var binding = _bindingEditor.CurrentBinding;
        SelectedRemoteButtonTitle.Text = GetButtonName(selectedButton);
        SelectedBindingDetail.Text =
            $"{GetTriggerName(_bindingEditor.SelectedTrigger)} → {GetActionName(binding.Action)}" +
            (string.IsNullOrWhiteSpace(binding.Argument) ? string.Empty : $" · {binding.Argument}");
        ActivePresetText.Text = $"当前起步模板：{_bindingEditor.Profile.Name} · 修改自动保存";
        UpdateHotspotSelection();
        UpdateTriggerSelection();
        UpdateActionSelection(binding.Action);
        UpdateArgumentEditor(binding);
    }

    private void SelectRemoteButton(RemoteButton button, string source)
    {
        _bindingEditor.SelectButton(button);
        if (!SupportsGesture(button, _bindingEditor.SelectedTrigger))
            _bindingEditor.SelectTrigger(Enum.GetValues<RemoteButtonTrigger>().First(trigger => SupportsGesture(button, trigger)));
        _selectedHotspotName = button.ToString();
        BindingActionPanel.IsEnabled = true;
        SelectedHardwareLabel.Text = $"当前键位：{GetButtonName(button)} · {source}选中";
        UpdateBindingSelection();
    }

    private void UpdateHotspotSelection()
    {
        foreach (var hotspot in RemoteHotspotCanvas.Children.OfType<System.Windows.Controls.Button>())
        {
            var selected = hotspot.Tag is string name && name == _selectedHotspotName;
            hotspot.Background = selected ? FindBrush("AccentSoftBrush") : Brushes.Transparent;
            hotspot.BorderBrush = selected ? FindBrush("AccentBrush") : Brushes.Transparent;
            hotspot.BorderThickness = new Thickness(selected ? 3 : 2);
        }
    }

    private void UpdateTriggerSelection()
    {
        foreach (var button in TriggerPanel.Children.OfType<System.Windows.Controls.Button>())
        {
            button.IsEnabled = button.Tag is string candidate && Enum.TryParse<RemoteButtonTrigger>(candidate, out var parsed) &&
                _bindingEditor.SelectedButton is { } selectedButton && SupportsGesture(selectedButton, parsed);
            var selected = button.Tag is string triggerName &&
                Enum.TryParse<RemoteButtonTrigger>(triggerName, out var trigger) &&
                trigger == _bindingEditor.SelectedTrigger;
            button.Background = selected ? FindBrush("AccentBrush") : FindBrush("SurfaceBrush");
            button.Foreground = selected ? Brushes.White : FindBrush("TextBrush");
            button.BorderBrush = selected ? FindBrush("AccentBrush") : FindBrush("BorderBrush");
        }
    }

    private void UpdateActionSelection(RemoteButtonAction selectedAction)
    {
        foreach (var button in FindVisualChildren<System.Windows.Controls.Button>(BindingActionPanel))
        {
            var selected = button.Tag is string actionName &&
                Enum.TryParse<RemoteButtonAction>(actionName, out var action) &&
                action == selectedAction;
            button.Background = selected ? FindBrush("AccentSoftBrush") : FindBrush("SurfaceBrush");
            button.BorderBrush = selected ? FindBrush("AccentBrush") : FindBrush("BorderBrush");
            button.BorderThickness = new Thickness(selected ? 2 : 1);
        }
    }

    private void UpdateArgumentEditor(RemoteButtonBinding binding)
    {
        var acceptsArgument = binding.Action == RemoteButtonAction.InsertSkill;
        BindingArgumentCard.Visibility = acceptsArgument
            ? Visibility.Visible
            : Visibility.Collapsed;
        if (!acceptsArgument)
        {
            return;
        }

        BindingArgumentTitle.Text = "Skill 内容";
        BindingArgumentHint.Text = "例如 $baseline-packager；Claude Code 也可填写 /skill";
        BindingArgumentTextBox.Text = binding.Argument ?? string.Empty;
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject parent)
        where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match)
            {
                yield return match;
            }

            foreach (var descendant in FindVisualChildren<T>(child))
            {
                yield return descendant;
            }
        }
    }

    private static RemoteBindingProfile GetPreset(string name)
    {
        return name == "Claude Code"
            ? RemoteBindingPresets.CreateClaudeCode()
            : RemoteBindingPresets.CreateCodex();
    }

    private static string GetTriggerName(RemoteButtonTrigger trigger)
    {
        return trigger switch
        {
            RemoteButtonTrigger.SingleClick => "单击",
            RemoteButtonTrigger.DoubleClick => "双击",
            RemoteButtonTrigger.LongPress => "长按",
            _ => trigger.ToString(),
        };
    }

    private static string GetActionName(RemoteButtonAction action)
    {
        return action switch
        {
            RemoteButtonAction.Disabled => "不执行",
            RemoteButtonAction.Escape => "取消/退出",
            RemoteButtonAction.SubmitTask => "发送任务",
            RemoteButtonAction.NewLine => "换行",
            RemoteButtonAction.ScrollUp => "目录上滑",
            RemoteButtonAction.ScrollDown => "目录下滑",
            RemoteButtonAction.ContextUp => "智能上移/左侧对话上滑",
            RemoteButtonAction.ContextDown => "智能下移/左侧对话下滑",
            RemoteButtonAction.NavigatePrevious => "上一项",
            RemoteButtonAction.NavigateNext => "下一项",
            RemoteButtonAction.PreviousProject => "上一个运行项目",
            RemoteButtonAction.NextProject => "下一个运行项目",
            RemoteButtonAction.GoBack => "返回",
            RemoteButtonAction.FocusPrompt => "聚焦输入框",
            RemoteButtonAction.Find => "快捷查找",
            RemoteButtonAction.Undo => "快捷回滚",
            RemoteButtonAction.Redo => "重做",
            RemoteButtonAction.SelectModel => "选择模型",
            RemoteButtonAction.SelectReasoning => "切换推理强度",
            RemoteButtonAction.InsertSkill => "录入 Skill",
            RemoteButtonAction.NewChat => "新建对话",
            RemoteButtonAction.NewStandaloneChat => "新建独立任务",
            RemoteButtonAction.PreviousChat => "上一个对话",
            RemoteButtonAction.NextChat => "下一个对话",
            RemoteButtonAction.NextChatNeedingAttention => "下一个待处理对话",
            RemoteButtonAction.OpenCommandMenu => "打开活动视图",
            RemoteButtonAction.OpenProjectPicker => "选择项目",
            RemoteButtonAction.OpenProjectTools => "打开项目工具",
            RemoteButtonAction.ToggleSidebar => "显示/隐藏侧边栏",
            RemoteButtonAction.ToggleTerminal => "显示/隐藏终端",
            RemoteButtonAction.SearchFiles => "搜索文件",
            RemoteButtonAction.ToggleFileTree => "显示/隐藏文件树",
            RemoteButtonAction.OpenReview => "打开代码审查",
            RemoteButtonAction.ToggleReviewPanel => "显示/隐藏审查面板",
            RemoteButtonAction.OpenBrowser => "打开内置浏览器",
            RemoteButtonAction.OpenSettings => "打开 Codex 设置",
            RemoteButtonAction.ApproveRequest => "批准当前请求",
            RemoteButtonAction.DeclineRequest => "拒绝当前请求",
            RemoteButtonAction.OpenCodex => "打开 Codex",
            RemoteButtonAction.OpenClaudeCode => "打开 Claude Code",
            RemoteButtonAction.VolumeUp => "系统音量加",
            RemoteButtonAction.VolumeDown => "系统音量减",
            _ => action.ToString(),
        };
    }

    private void OpenBluetoothSettings_Click(object sender, RoutedEventArgs e)
    {
        Process.Start(new ProcessStartInfo("ms-settings:bluetooth")
        {
            UseShellExecute = true,
        });
    }

    private void OpenControlsSettings_Click(object sender, RoutedEventArgs e)
    {
        ShowOnboardingStep(OnboardingStep.Controls);
        ControlsDetail.Text = _settings.RemoteActionsEnabled
            ? "按键设置已启用；真机报告会在这里实时显示"
            : "设置会自动保存；点击“保存并启用”后即可使用，真机验证可随时继续";
    }

    private async void OpenAudioTest_Click(object sender, RoutedEventArgs e)
    {
        ShowOnboardingStep(OnboardingStep.Audio);
        await InitializeAudioAsync();
    }

    private async void EnableButtonTest_Click(object sender, RoutedEventArgs e)
    {
        var state = Rc003PresenceProbe.GetState();
        if (!state.BluetoothConnected)
        {
            _bridgeError = "请先在 Windows 蓝牙设置中连接遥控器";
            UpdateUi(state);
            return;
        }

        if (!File.Exists(HidBridgeRuntimePaths.SharedEventLogPath))
        {
            _bridgeError = "后台按键桥接尚未安装；请运行一次 VoiceAnything 安装程序";
            UpdateUi(state);
            return;
        }

        _bridgeError = null;
        ReadBridgeLog();
        await RefreshStatusAsync();
    }

    private async Task RefreshStatusAsync()
    {
        if (_isRefreshing)
        {
            return;
        }

        _isRefreshing = true;
        try
        {
            var state = await Task.Run(Rc003PresenceProbe.GetState);
            ReadBridgeLog();
            ExecuteGestures(_gestureRecognizer.Flush(DateTimeOffset.Now));
            _capabilities.RemoteConnected = state.BluetoothConnected;
            if (!state.BluetoothConnected)
            {
                _gestureRecognizer.Reset();
                _statisticsHeld.Clear();
                FinishUsageSession();
                StopVoiceInputSession();
                _reflectionCapture?.Cancel();
                if (_atvvClient.IsConnected) await _atvvClient.DisconnectAsync();
                _bridgeStarting = false;
                _hookReady = false;
                _lastButton = null;
                _capabilities.RemoteButtonObserved = false;
            }

            UpdateUi(state);
            await EnsureVoiceRuntimeAsync(state);
        }
        catch (Exception exception) when (
            exception is Win32Exception or IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            _bridgeError = $"设备状态读取失败：{exception.Message}";
            UpdateUi(new Rc003PresenceState(false, false, null));
        }
        finally
        {
            _isRefreshing = false;
        }
    }

    private async Task EnsureVoiceRuntimeAsync(Rc003PresenceState state)
    {
        if (_selectedProfile?.Profile.Capabilities.Voice != true) return;
        if (!state.BluetoothConnected)
        {
            _nextAudioReconnectAt = DateTimeOffset.MinValue;
            return;
        }

        if (!VoiceRuntimeStartupPolicy.ShouldConnect(
                state.BluetoothConnected,
                _settings.PreferredInputMethodId,
                _atvvClient.IsConnected,
                _audioInitializing) ||
            DateTimeOffset.Now < _nextAudioReconnectAt)
        {
            return;
        }

        _nextAudioReconnectAt = DateTimeOffset.Now.AddSeconds(5);
        await InitializeAudioAsync();
        if (_atvvClient.IsConnected)
        {
            _nextAudioReconnectAt = DateTimeOffset.MaxValue;
        }
    }

    private void ReadBridgeLog(bool applyReports = true)
    {
        if (!File.Exists(_bridgeLogPath))
        {
            return;
        }

        string[] lines;
        try
        {
            using var stream = new FileStream(
                _bridgeLogPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            var content = reader.ReadToEnd();
            lines = content.Split(
                ['\r', '\n'],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }
        catch (IOException)
        {
            return;
        }

        if (lines.Length < _processedLogLineCount)
        {
            _processedLogLineCount = 0;
        }

        foreach (var line in lines.Skip(_processedLogLineCount))
        {
            var logEvent = HidBridgeLogEventCodec.ParseLine(line);
            if (logEvent is not null && (applyReports || logEvent.Kind != "hid_report"))
            {
                ApplyBridgeEvent(logEvent);
            }
        }

        _processedLogLineCount = lines.Length;
    }

    private void ApplyBridgeEvent(HidBridgeLogEvent logEvent)
    {
        var selection = DeviceSelection.Read();
        var key = selection is null ? null : DeviceSelection.Key(selection.BluetoothAddress);
        if ((logEvent.DeviceKey is not null && logEvent.DeviceKey != key) ||
            (logEvent.Kind is "hook_ready" or "hid_report" && (key is null || logEvent.DeviceKey != key))) return;
        if (logEvent.Kind is "bridge_error" or "bridge_stopped")
        {
            _gestureRecognizer.Reset();
            _statisticsHeld.Clear();
            if (_microphoneHidPressed)
            {
                _microphoneHidPressed = false;
                FinishUsageSession();
                StopVoiceInputSession();
                _voiceToggle.Reset();
                _ = _atvvClient.DisconnectAsync();
            }
        }
        switch (logEvent.Kind)
        {
            case "bridge_starting":
                _bridgeStarting = true;
                _bridgeError = null;
                _bridgeProgressDetail = null;
                break;
            case "gadget_connect_timeout":
                _bridgeStarting = true;
                _bridgeError = null;
                _bridgeProgressDetail = "检测到旧桥接会话，正在自动恢复…";
                break;
            case "device_restart_started":
                _bridgeStarting = true;
                _bridgeError = null;
                _bridgeProgressDetail = "正在重新加载 RC003MS 按键服务…";
                break;
            case "device_restart_completed":
                _bridgeStarting = true;
                _bridgeError = null;
                _bridgeProgressDetail = "按键服务已恢复，正在完成连接…";
                break;
            case "hook_ready":
                _bridgeStarting = false;
                _hookReady = true;
                _bridgeError = null;
                _bridgeProgressDetail = null;
                break;
            case "bridge_error":
                _bridgeStarting = false;
                _hookReady = false;
                _bridgeProgressDetail = null;
                _bridgeError = string.IsNullOrWhiteSpace(logEvent.Detail)
                    ? "按键桥接未能启动"
                    : $"按键桥接未能启动：{logEvent.Detail}";
                break;
            case "bridge_stopped":
                _bridgeStarting = false;
                _hookReady = false;
                _bridgeProgressDetail = null;
                break;
            case "hid_report" when _hookReady:
                var usages = RemoteHidReportParser.ParseUsages(
                    logEvent.ReportId,
                    logEvent.Payload);
                var timestamp = logEvent.Timestamp == default
                    ? DateTimeOffset.Now
                    : logEvent.Timestamp;
                var microphoneUsage = _selectedProfile?.Profile.Controls.FirstOrDefault(control => control.Gestures.Contains("voice"))?.Usage;
                var microphonePressed = microphoneUsage is ushort voiceUsage && usages?.Contains(voiceUsage) == true;
                CountPhysicalButtons(usages, timestamp);
                var microphoneWasPressed = _microphoneHidPressed;
                _microphoneHidPressed = microphonePressed;
                if (microphonePressed && !microphoneWasPressed)
                {
                    HandleRemoteMicrophonePressed(timestamp);
                }
                else if (!microphonePressed && microphoneWasPressed)
                {
                    HandleRemoteMicrophoneReleased(timestamp);
                }
                var buttons = usages?
                    .Select(ProfileButton)
                    .Where(candidate => candidate is not null)
                    .Select(candidate => candidate!.Value)
                    .Distinct()
                    .ToArray() ?? [];
                var button = buttons.Cast<RemoteButton?>().FirstOrDefault();
                if (button is not null)
                {
                    _lastButton = button;
                    _capabilities.RemoteButtonObserved = true;
                    if (_currentStep == OnboardingStep.Controls)
                    {
                        SelectRemoteButton(button.Value, "真机");
                    }
                    if (_currentStep == OnboardingStep.Controls && _testedButtons.Add(button.Value))
                    {
                        _capabilities.TestedRemoteButtonCount = _testedButtons.Count;
                        ControlsDetail.Text =
                            $"已识别 {_testedButtons.Count}/3：{string.Join("、", _testedButtons.Select(GetButtonName))}";
                        ControlsProgress.Value = Math.Min(_testedButtons.Count, 3);
                        FinishButton.IsEnabled = OnboardingFlowPolicy.CanContinue(
                            OnboardingStep.Controls,
                            _capabilities);
                    }
                }

                ExecuteGestures(_gestureRecognizer.Process(buttons, timestamp));
                break;
        }
    }

    private void ExecuteGestures(IReadOnlyList<RemoteButtonGesture> gestures)
    {
        if (!_settings.RemoteActionsEnabled)
        {
            return;
        }

        foreach (var gesture in gestures)
        {
            if (!SupportsGesture(gesture.Button, gesture.Trigger)) continue;
            var binding = _bindingEditor.Profile.Resolve(gesture.Button, gesture.Trigger);
            try
            {
                var result = _actionExecutor.Execute(binding, _bindingEditor.Profile.Name);
                ButtonDetail.Text = result.Executed
                    ? $"{GetButtonName(gesture.Button)} · {GetActionName(binding.Action)}"
                    : result.Message;
            }
            catch (Win32Exception exception)
            {
                ButtonDetail.Text = $"按键动作执行失败：{exception.Message}";
            }
        }
    }

    private string? StartVoiceInputSession(WindowsVoiceInputProfile profile)
    {
        StopVoiceInputSession();
        try
        {
            var captureDeviceId = _audioOutput.CaptureDeviceId
                ?? throw new InvalidOperationException("CABLE Output 语音端点尚未就绪。" );
            _captureRoute = DefaultAudioCaptureRoute.Open(captureDeviceId);
            BeginReflection();
            WindowsVoiceInputActivator.Start(profile);
            _activeVoiceInputProfile = profile;
            return null;
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or COMException or UnauthorizedAccessException)
        {
            _captureRoute?.Dispose();
            _captureRoute = null;
            return exception.Message;
        }
    }

    private void StopVoiceInputSession()
    {
        if (_activeVoiceInputProfile is not null)
        {
            WindowsVoiceInputActivator.Stop();
            _activeVoiceInputProfile = null;
            if (_reflectionCapture is not null) _ = _reflectionCapture.FinishAsync();
        }

        _captureRoute?.Dispose();
        _captureRoute = null;
    }

    private void UpdateUi(Rc003PresenceState state)
    {
        ConnectionBadge.Background = FindBrush(
            state.BluetoothConnected ? "SuccessSoftBrush" : "PendingSoftBrush");
        ConnectionBadgeText.Foreground = FindBrush(
            state.BluetoothConnected ? "SuccessBrush" : "PendingBrush");
        ConnectionBadgeText.Text = state.BluetoothConnected ? "蓝牙已连接" : "未连接";
        ConnectionDetail.Text = state.BluetoothConnected
            ? "Windows 已连接“小米蓝牙语音遥控器”"
            : "未找到活动的 RC003MS，请先在蓝牙设置中连接遥控器";

        EnableButtonTestButton.IsEnabled =
            state.BluetoothConnected && File.Exists(_bridgeLogPath) && !_bridgeStarting && !_hookReady;
        EnableButtonTestButton.Content = _hookReady
            ? "本地按键测试已就绪"
            : _bridgeStarting
                ? "正在连接后台组件…"
                : "重新读取后台状态";

        if (_lastButton is not null)
        {
            ButtonDetail.Text = $"已识别：{GetButtonName(_lastButton.Value)}（真实按键）";
            ButtonStateDot.Background = FindBrush("SuccessBrush");
        }
        else if (!string.IsNullOrWhiteSpace(_bridgeError))
        {
            ButtonDetail.Text = _bridgeError;
            ButtonStateDot.Background = FindBrush("PendingBrush");
        }
        else if (_hookReady)
        {
            ButtonDetail.Text = "桥接已就绪，请按一次任意方向键";
            ButtonStateDot.Background = FindBrush("AccentBrush");
        }
        else if (_bridgeStarting)
        {
            ButtonDetail.Text = _bridgeProgressDetail ??
                "正在等待系统授权并启动按键桥接…";
            ButtonStateDot.Background = FindBrush("AccentBrush");
        }
        else if (state.BluetoothConnected)
        {
            ButtonDetail.Text = !File.Exists(_bridgeLogPath)
                ? "后台按键组件未安装，请运行一次完整安装程序"
                : state.HidServicePresent
                    ? "蓝牙已连接，正在等待后台按键组件就绪"
                    : "蓝牙已连接，正在等待 Windows 加载按键服务";
            ButtonStateDot.Background = new SolidColorBrush(Color.FromRgb(183, 192, 202));
        }
        else
        {
            ButtonDetail.Text = "连接完成后才能开始真实按键测试";
            ButtonStateDot.Background = new SolidColorBrush(Color.FromRgb(183, 192, 202));
        }

        ContinueButton.IsEnabled = OnboardingFlowPolicy.CanContinue(
            OnboardingStep.Remote,
            _capabilities);
    }

    private string GetButtonName(RemoteButton button)
    {
        var label = _selectedProfile?.Profile.Controls.FirstOrDefault(control => control.Id == button.Id)?.Label;
        if (label is not null) return label;
        return button.Id switch
        {
            "Power" => "电源键",
            "Up" => "方向上",
            "Left" => "方向左",
            "Ok" => "确认键",
            "Right" => "方向右",
            "Down" => "方向下",
            "Back" => "返回键",
            "VolumeUp" => "音量加",
            "Home" => "主页键",
            "VolumeDown" => "音量减",
            "Menu" => "菜单键",
            "Tv" => "电视键",
            _ => button.ToString(),
        };
    }

    private string SaveCapturedVoice()
    {
        var recordingDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "VoiceAnything",
            "Recordings");
        Directory.CreateDirectory(recordingDirectory);
        var path = Path.Combine(
            recordingDirectory,
            $"remote-test-{DateTimeOffset.Now:yyyyMMdd-HHmmss-fff}.wav");
        File.WriteAllBytes(
            path,
            PcmWaveFile.BuildMono16(_voiceCapture.Snapshot(), sampleRate: 16_000));
        return path;
    }

    private static string FormatDb(double value)
    {
        return double.IsFinite(value) ? value.ToString("0.0") : "−∞";
    }

    private static double ToMeterValue(double peakDbFs)
    {
        return double.IsFinite(peakDbFs)
            ? Math.Clamp((peakDbFs + 60d) / 60d * 100d, 0d, 100d)
            : 0d;
    }

    private Brush FindBrush(string key)
    {
        return (Brush)FindResource(key);
    }
}

public sealed record VoiceInputReadiness(string StatusText, bool CanTest);

public sealed record VoiceInputTestItem(
    WindowsVoiceInputProfile Profile,
    VoiceInputReadiness Readiness);

public static class VoiceInputReadinessPolicy
{
    public static VoiceInputReadiness Evaluate(
        WindowsVoiceInputKind kind,
        bool executableInstalled,
        bool appRunning,
        bool helperRunning)
    {
        if (!executableInstalled && kind != WindowsVoiceInputKind.CodexDictation)
        {
            return new VoiceInputReadiness("未安装", CanTest: false);
        }

        return kind switch
        {
            WindowsVoiceInputKind.Typeless when appRunning =>
                new VoiceInputReadiness("运行中 · 可测试", CanTest: true),
            WindowsVoiceInputKind.Typeless =>
                new VoiceInputReadiness("已安装 · 请先打开", CanTest: false),
            WindowsVoiceInputKind.CodexDictation when appRunning =>
                new VoiceInputReadiness("运行中 · 可测试", CanTest: true),
            WindowsVoiceInputKind.CodexDictation =>
                new VoiceInputReadiness("请先打开 Codex", CanTest: false),
            WindowsVoiceInputKind.WeChatVoice when appRunning =>
                new VoiceInputReadiness("运行中 · 可测试", CanTest: true),
            WindowsVoiceInputKind.WeChatVoice =>
                new VoiceInputReadiness("已安装 · 测试时自动启动", CanTest: true),
            WindowsVoiceInputKind.BageShuo when helperRunning =>
                new VoiceInputReadiness("快捷键服务已连接 · 可测试", CanTest: true),
            WindowsVoiceInputKind.BageShuo when appRunning =>
                new VoiceInputReadiness("已打开 · 测试时检查快捷键", CanTest: true),
            WindowsVoiceInputKind.BageShuo =>
                new VoiceInputReadiness("已安装 · 测试时自动启动", CanTest: true),
            _ => new VoiceInputReadiness("不可用", CanTest: false),
        };
    }
}

public static class InputDispatchPolicy
{
    private static readonly TimeSpan DoubleClickWindow = TimeSpan.FromMilliseconds(320);

    public static TimeSpan GestureFlushInterval { get; } = TimeSpan.FromMilliseconds(40);

    public static TimeSpan MaximumSingleClickDispatchDelay =>
        DoubleClickWindow + GestureFlushInterval;
}
