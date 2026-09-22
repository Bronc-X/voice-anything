using System.IO;
using System.Text.Json;
using System.Windows;
using SayAll.Core.History;
using SayAll.Core.Onboarding;

namespace SayAll.Windows;

public partial class MainWindow
{
    private readonly JournalStore _journal = new(JournalStore.DefaultDirectory);
    private readonly HashSet<ushort> _statisticsHeld = [];
    private ReflectionCapture? _reflectionCapture;
    private InsightsWindow? _insightsWindow;
    private DateTimeOffset? _statisticsVoiceStart;
    private readonly HashSet<Task> _pendingJournalWrites = [];

    private void InitializeHistory()
    {
        _reflectionCapture = new ReflectionCapture(_journal,
            message => Dispatcher.InvokeAsync(() => HistoryStatusText.Text = message));
    }

    private void OpenHistory_Click(object sender, RoutedEventArgs e)
    {
        if (_insightsWindow is not null) { _insightsWindow.Activate(); return; }
        _insightsWindow = new InsightsWindow(_journal) { Owner = this };
        _insightsWindow.PrivacyChanged += () => _reflectionCapture?.Cancel();
        _insightsWindow.Closed += (_, _) => _insightsWindow = null;
        _insightsWindow.Show();
    }

    private void CountPhysicalButtons(IReadOnlySet<ushort>? usages, DateTimeOffset timestamp)
    {
        if (usages is null) return;
        var actual = usages.Where(usage => _selectedProfile?.Profile.Controls.Any(control => control.Usage == usage) == true).ToHashSet();
        var count = actual.Count(usage => !_statisticsHeld.Contains(usage));
        _statisticsHeld.Clear();
        _statisticsHeld.UnionWith(actual);
        if (count > 0) WriteJournal(() => { for (var index = 0; index < count; index++) _journal.RecordButton(timestamp, TimeZoneInfo.Local); });
    }

    private void FinishUsageSession()
    {
        if (_statisticsVoiceStart is not DateTimeOffset start) return;
        _statisticsVoiceStart = null;
        var seconds = _voiceCapture.DurationSeconds(16_000d);
        if (seconds <= 0) return;
        var end = DateTimeOffset.Now;
        WriteJournal(() => _journal.RecordVoice(start, end < start ? start : end, seconds, TimeZoneInfo.Local));
    }

    private async void WriteJournal(Action write)
    {
        var pending = Task.Run(write);
        _pendingJournalWrites.Add(pending);
        try { await pending; }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or ArgumentException or InvalidOperationException)
        { HistoryStatusText.Text = "记录未保存：" + exception.Message; }
        finally { _pendingJournalWrites.Remove(pending); }
    }

    private async Task FlushJournalAsync()
    {
        try { await Task.WhenAll(_pendingJournalWrites.ToArray()); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or ArgumentException or InvalidOperationException)
        { MessageBox.Show("使用记录未能全部保存：" + error.Message, "Voice Anything", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    private void BeginReflection()
    {
        try { _reflectionCapture?.Start(_selectedProfile?.Profile.Id ?? "xiaomi-rc003"); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        { HistoryStatusText.Text = "回眸未启用：" + exception.Message; }
    }

    private bool IsRecordingTest => _currentStep is OnboardingStep.Audio or OnboardingStep.VoiceTest;
}
