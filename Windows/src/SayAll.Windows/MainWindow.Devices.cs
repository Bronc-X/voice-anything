using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SayAll.Core.Devices;
using SayAll.Core.Input;
using SayAll.HidBridge.Contracts;

namespace SayAll.Windows;

public partial class MainWindow
{
    private InstalledDeviceProfile? _selectedProfile;
    private DevicesWindow? _devicesWindow;

    private void LoadDeviceProfiles()
    {
        try
        {
            var profiles = DevicesWindow.Profiles();
            var selection = DeviceSelection.Read();
            _selectedProfile = selection is null ? profiles.FirstOrDefault() : profiles.FirstOrDefault(item => item.Profile.Id == selection.ProfileId);
            if (selection is not null && _selectedProfile is null)
                throw new InvalidDataException("已保存设备的型号包缺失，请重新选择型号。");
            if (_selectedProfile is not null) RenderDeviceProfile(_selectedProfile);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            _selectedProfile = null;
            RemoteHotspotCanvas.Children.Clear();
            HomeDeviceImage.Source = null;
            SelectedDeviceTitle.Text = "未选择支持的型号";
            HistoryStatusText.Text = "型号加载失败：" + error.Message;
        }
    }
    private void OpenDevices_Click(object sender, RoutedEventArgs e)
    {
        if (_devicesWindow is not null) { _devicesWindow.Activate(); return; }
        _devicesWindow = new DevicesWindow { Owner = this };
        _devicesWindow.Selected += async profile =>
        {
            _changingDevice = true;
            try
            {
            await CancelAudioInitializationAsync();
            await SaveSettingsAsync();
            StopVoiceInputSession();
            FinishUsageSession();
            _reflectionCapture?.Cancel();
            _statisticsHeld.Clear();
            _microphoneHidPressed = false;
            _hookReady = false;
            _selectedProfile = profile;
            RenderDeviceProfile(profile);
            ApplyModelBindings();
            await _atvvClient.DisconnectAsync();
            }
            finally { _changingDevice = false; }
            await InitializeAudioAsync();
        };
        _devicesWindow.Closed += (_, _) => _devicesWindow = null;
        _devicesWindow.Show();
    }
    private void RenderDeviceProfile(InstalledDeviceProfile installed)
    {
        var profile = installed.Profile;
        SelectedDeviceTitle.Text = profile.Name;
        RemoteHotspotCanvas.Children.Clear();
        const double height = 400;
        var width = height * profile.AspectRatio;
        RemoteHotspotCanvas.Width = width;
        HomeDeviceImage.Source = null;
        if (profile.ResolveArtwork(installed.Directory) is string artwork)
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit(); bitmap.DecodePixelWidth = 1024; bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.UriSource = new Uri(artwork); bitmap.EndInit(); bitmap.Freeze();
            HomeDeviceImage.Source = bitmap;
            RemoteHotspotCanvas.Children.Add(new Image { Width = width, Height = height, Source = bitmap, Stretch = Stretch.Fill });
        }
        else RemoteHotspotCanvas.Children.Add(new Border { Width = width, Height = height, Background = FindBrush("SurfaceAltBrush"), CornerRadius = new CornerRadius(28), BorderBrush = FindBrush("BorderBrush"), BorderThickness = new Thickness(1) });
        foreach (var control in profile.Controls)
        {
            var hotspot = new Button
            {
                Tag = control.Id, ToolTip = control.Label, Width = control.Width * width, Height = control.Height * height,
                Style = (Style)RemoteHotspotCanvas.FindResource("RemoteHotspotStyle"),
                Content = profile.Artwork is null ? control.Label : null,
            };
            Canvas.SetLeft(hotspot, control.X * width); Canvas.SetTop(hotspot, control.Y * height);
            hotspot.Click += RemoteHotspot_Click;
            RemoteHotspotCanvas.Children.Add(hotspot);
        }
    }
    private RemoteButton? ProfileButton(ushort usage)
    {
        var control = _selectedProfile?.Profile.Controls.FirstOrDefault(item => item.Usage == usage);
        return control is not null && !control.Gestures.Contains("voice") && RemoteButton.TryParse(control.Id, out var button) ? button : null;
    }

    private void ApplyModelBindings()
    {
        if (_selectedProfile is null) return;
        var profile = _selectedProfile.Profile;
        var binding = _settings.ModelBindings.GetValueOrDefault(profile.Id);
        _bindingEditor.ApplyProfile(binding is not null ? new RemoteBindingProfile(binding.Preset, binding.Bindings)
            : profile.Id == "xiaomi-rc003" ? new RemoteBindingProfile(_settings.ActivePreset, _settings.RemoteBindings)
            : new RemoteBindingProfile("自定义", []));
        _settings.ActivePreset = _bindingEditor.Profile.Name;
        _gestureRecognizer = new RemoteButtonGestureRecognizer(doubleClickButtons: profile.Controls
            .Where(control => control.Gestures.Contains("double")).Select(control => new RemoteButton(control.Id)).ToArray(),
            longPressButtons: profile.Controls.Where(control => control.Gestures.Contains("long")).Select(control => new RemoteButton(control.Id)).ToArray());
        _testedButtons.Clear();
        _selectedHotspotName = null;
        BindingActionPanel.IsEnabled = false;
        SelectedRemoteButtonTitle.Text = "选择一个按键";
        SelectedBindingDetail.Text = "点击设备上的按键，设置它的动作。";
        ActivePresetText.Text = $"当前起步模板：{_bindingEditor.Profile.Name}";
    }

    private bool SupportsGesture(RemoteButton button, RemoteButtonTrigger trigger)
    {
        var gesture = trigger switch { RemoteButtonTrigger.SingleClick => "single", RemoteButtonTrigger.DoubleClick => "double", _ => "long" };
        return _selectedProfile?.Profile.Controls.Any(control => control.Id == button.Id && control.Gestures.Contains(gesture)) == true;
    }
}
