using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using SayAll.Core.Audio;
using SayAll.Core.Devices;
using SayAll.HidBridge.Contracts;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Enumeration;
using Windows.Storage.Streams;

namespace SayAll.Windows;

public partial class DevicesWindow : Window
{
    public event Action<InstalledDeviceProfile>? Selected;
    private bool busy;
    private readonly CancellationTokenSource lifetime = new();
    private static string UserProfiles => Path.Combine(SayAll.Core.History.JournalStore.DefaultDirectory, "devices");
    private static void ValidateArtwork(DeviceProfile profile, string directory)
    {
        if (profile.ResolveArtwork(directory) is not string path) return;
        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.DecodePixelWidth = 1024;
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.UriSource = new Uri(path);
            bitmap.EndInit();
            if (bitmap.PixelWidth <= 0 || bitmap.PixelHeight <= 0) throw new InvalidDataException("型号图片为空。");
        }
        catch (Exception error) when (error is FormatException or NotSupportedException)
        { throw new InvalidDataException("型号图片无法解码，请使用有效的 PNG 或 JPEG。", error); }
    }
    public static IReadOnlyList<InstalledDeviceProfile> Profiles()
    {
        var profiles = DeviceProfileCatalog.LoadDirectory(Path.Combine(AppContext.BaseDirectory, "devices"))
            .Concat(DeviceProfileCatalog.LoadDirectory(UserProfiles)).ToArray();
        if (profiles.Select(item => item.Profile.Id).Distinct().Count() != profiles.Length)
            throw new InvalidDataException("内置型号与导入型号存在重复 ID，请移除重复包。");
        foreach (var item in profiles) ValidateArtwork(item.Profile, item.Directory);
        return profiles;
    }
    public DevicesWindow()
    {
        InitializeComponent();
        Closed += (_, _) => { lifetime.Cancel(); lifetime.Dispose(); };
        try { ProfileList.ItemsSource = Profiles(); ProfileList.SelectedIndex = 0; }
        catch (Exception error) when (error is IOException or System.Text.Json.JsonException or UnauthorizedAccessException)
        { StatusText.Text = "型号加载失败：" + error.Message; }
    }
    private void Profile_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (ProfileList.SelectedItem is InstalledDeviceProfile selected)
            CapabilityText.Text = $"{selected.Profile.Controls.Length} 个控件 · " +
                (selected.Profile.Capabilities.Voice ? "按住说话" : "仅按键") +
                $"\nWindows：{selected.Profile.Validation.GetValueOrDefault("windows", "research")}\n导入配置不代表已通过真机验收。";
    }
    private async void Scan_Click(object sender, RoutedEventArgs e)
    {
        if (busy) return;
        busy = true; ConnectButton.IsEnabled = false;
        StatusText.Text = "正在扫描已配对的蓝牙设备…";
        try
        {
            using var operation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
            operation.CancelAfter(TimeSpan.FromSeconds(15));
            var devices = await DeviceInformation.FindAllAsync(BluetoothLEDevice.GetDeviceSelectorFromPairingState(true)).AsTask(operation.Token);
            DeviceList.ItemsSource = devices.ToArray();
            StatusText.Text = devices.Count == 0 ? "没有已配对的蓝牙设备。请先在系统设置中完成配对。" : $"找到 {devices.Count} 台已配对设备，请选择要使用的遥控器。";
        }
        catch (OperationCanceledException) { StatusText.Text = "扫描已取消或超时，请确认蓝牙已开启后重试。"; }
        catch (Exception error) when (error is COMException or UnauthorizedAccessException)
        { StatusText.Text = "扫描失败：" + error.Message; }
        finally { busy = false; ConnectButton.IsEnabled = true; }
    }
    private async void Connect_Click(object sender, RoutedEventArgs e)
    {
        if (busy) return;
        if (ProfileList.SelectedItem is not InstalledDeviceProfile profile || DeviceList.SelectedItem is not DeviceInformation information)
        { StatusText.Text = "请选择型号和一台已配对的设备。"; return; }
        busy = true; ConnectButton.IsEnabled = false; StatusText.Text = "正在读取实际型号与 ATVV 能力…";
        ProfileList.IsEnabled = false; DeviceList.IsEnabled = false;
        try
        {
            using var operation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
            operation.CancelAfter(TimeSpan.FromSeconds(30));
            var cancellationToken = operation.Token;
            using var device = await BluetoothLEDevice.FromIdAsync(information.Id).AsTask(cancellationToken)
                ?? throw new InvalidOperationException("设备无法打开，请确认蓝牙连接。");
            if (!device.DeviceInformation.Pairing.IsPaired) throw new InvalidOperationException("设备尚未配对。");
            var services = await device.GetGattServicesForUuidAsync(Guid.Parse("0000180a-0000-1000-8000-00805f9b34fb"), BluetoothCacheMode.Uncached).AsTask(cancellationToken);
            string? model = null;
            try
            {
                if (services.Status != GattCommunicationStatus.Success) throw new InvalidOperationException("无法读取设备信息服务。");
                foreach (var service in services.Services)
                {
                    var chars = await service.GetCharacteristicsForUuidAsync(Guid.Parse("00002a24-0000-1000-8000-00805f9b34fb"), BluetoothCacheMode.Uncached).AsTask(cancellationToken);
                    if (chars.Status != GattCommunicationStatus.Success) continue;
                    foreach (var characteristic in chars.Characteristics)
                    {
                        var value = await characteristic.ReadValueAsync(BluetoothCacheMode.Uncached).AsTask(cancellationToken);
                        if (value.Status != GattCommunicationStatus.Success) continue;
                        using var reader = DataReader.FromBuffer(value.Value);
                        model = reader.ReadString(value.Value.Length).Trim('\0', ' ', '\r', '\n');
                    }
                }
            }
            finally { foreach (var service in services.Services) service.Dispose(); }
            if (model is null || !profile.Profile.MatchesModel(model)) throw new InvalidOperationException("实际型号与所选型号包不匹配，未更改当前连接。");
            var audio = await device.GetGattServicesForUuidAsync(AtvvProtocol.ServiceUuid, BluetoothCacheMode.Uncached).AsTask(cancellationToken);
            try { if (audio.Status != GattCommunicationStatus.Success || audio.Services.Count != 1) throw new InvalidOperationException("未发现唯一 ATVV 语音服务。"); }
            finally { foreach (var service in audio.Services) service.Dispose(); }
            var selection = new SelectedRemote(profile.Profile.Id, device.BluetoothAddress.ToString("X12"),
                profile.Profile.Transport?.HardwareToken ?? DeviceSelection.DefaultHardwareToken);
            if (!Rc003PresenceProbe.GetState(selection).HidServicePresent)
                throw new InvalidOperationException("设备的实际 HID 标识与型号包不符，未更改当前选择。");
            await using (var probe = new Rc003AtvvClient())
            {
                var capability = new TaskCompletionSource<AtvvCapabilities>(TaskCreationOptions.RunContinuationsAsynchronously);
                probe.CapabilitiesReceived += value => capability.TrySetResult(value);
                probe.Failed += error => capability.TrySetException(error);
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(12));
                await probe.ConnectAsync(timeout.Token, selection);
                await capability.Task.WaitAsync(timeout.Token);
            }
            cancellationToken.ThrowIfCancellationRequested();
            DeviceSelection.Save(selection);
            Selected?.Invoke(profile);
            StatusText.Text = "设备身份已保存，主窗口将连接所选设备。按键桥仍需通过当前驱动校验。";
        }
        catch (OperationCanceledException) { StatusText.Text = "读取设备能力超时。当前选择未改变，请唤醒遥控器后重试。"; }
        catch (Exception error) when (error is COMException or IOException or UnauthorizedAccessException or InvalidOperationException or System.Text.Json.JsonException)
        { StatusText.Text = "验证未完成：" + error.Message; }
        finally { busy = false; ConnectButton.IsEnabled = true; ProfileList.IsEnabled = true; DeviceList.IsEnabled = true; }
    }
    private void Import_Click(object sender, RoutedEventArgs e)
    {
        if (busy) return;
        var dialog = new OpenFolderDialog { Title = "选择包含 profile.json 的型号包文件夹" };
        if (dialog.ShowDialog(this) != true) return;
        string? temporary = null;
        try
        {
            var profile = DeviceProfile.Load(Path.Combine(dialog.FolderName, "profile.json"));
            ValidateArtwork(profile, dialog.FolderName);
            if (Profiles().Any(item => item.Profile.Id == profile.Id)) throw new InvalidDataException("该型号 ID 已存在，未覆盖已有配置。");
            Directory.CreateDirectory(UserProfiles);
            temporary = Path.Combine(UserProfiles, ".import-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temporary);
            File.Copy(Path.Combine(dialog.FolderName, "profile.json"), Path.Combine(temporary, "profile.json"));
            if (profile.ResolveArtwork(dialog.FolderName) is string artwork)
            {
                var destination = profile.ResolveArtwork(temporary)!;
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(artwork, destination);
            }
            _ = DeviceProfile.Load(Path.Combine(temporary, "profile.json"));
            Directory.Move(temporary, Path.Combine(UserProfiles, profile.Id));
            temporary = null;
            ProfileList.ItemsSource = Profiles();
            StatusText.Text = "型号包已导入。选择真实设备完成验证后才能连接。";
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or ArgumentException)
        { StatusText.Text = "导入失败：" + error.Message; }
        finally
        {
            if (temporary is not null && Directory.Exists(temporary))
            {
                // Only this operation's uniquely named staging directory can be removed.
                var allowed = Path.GetFullPath(UserProfiles) + Path.DirectorySeparatorChar;
                if (Path.GetFullPath(temporary).StartsWith(allowed, StringComparison.OrdinalIgnoreCase)) Directory.Delete(temporary, true);
            }
        }
    }
    private void Bluetooth_Click(object sender, RoutedEventArgs e) => Process.Start(new ProcessStartInfo("ms-settings:bluetooth") { UseShellExecute = true });
}
