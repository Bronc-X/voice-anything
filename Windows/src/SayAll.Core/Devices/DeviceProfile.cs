using System.Text.Json;
using System.Text.RegularExpressions;

namespace SayAll.Core.Devices;

public sealed record DeviceControl(string Id, string Label, ushort Usage, double X, double Y,
    double Width, double Height, string[] Gestures);
public sealed record DeviceCapabilities(bool Voice, bool HoldToTalk, bool ToggleVoice, bool Battery, bool Touch);
public sealed record DeviceTransport(ushort VendorId, ushort ProductId, ushort ProductVersion,
    byte VendorIdSource, string[] AdvertisedNames)
{
    [System.Text.Json.Serialization.JsonIgnore]
    public string HardwareToken => $"Dev_VID&{VendorIdSource:X2}{VendorId:X4}_PID&{ProductId:X4}_REV&{ProductVersion:X4}_";
}
public sealed record DeviceProfile(int SchemaVersion, string Id, string Name, string Adapter,
    string[] ModelNumbers, string? Artwork, double AspectRatio, DeviceCapabilities Capabilities,
    DeviceControl[] Controls, Dictionary<string, string> Validation, DeviceTransport? Transport = null)
{
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow,
        RespectRequiredConstructorParameters = true
    };

    public static DeviceProfile Load(string path)
    {
        var file = new FileInfo(path);
        if (file.Length > 128 * 1024) throw new InvalidDataException("型号文件不能超过 128 KB。");
        var profile = JsonSerializer.Deserialize<DeviceProfile>(File.ReadAllText(path), JsonOptions)
            ?? throw new InvalidDataException("型号文件为空。");
        profile.Validate();
        if (profile.Artwork is not null)
        {
            var image = profile.ResolveArtwork(Path.GetDirectoryName(Path.GetFullPath(path))!);
            if (!File.Exists(image) || new FileInfo(image).Length > 8 * 1024 * 1024)
                throw new InvalidDataException("型号图片不存在或大于 8 MB。");
        }
        return profile;
    }

    public void Validate()
    {
        if (SchemaVersion != 1 || !Regex.IsMatch(Id ?? "", "^[a-z0-9][a-z0-9-]{2,63}$") ||
            string.IsNullOrWhiteSpace(Name) || Name.Length > 80 || Adapter != "xiaomi-atvv-v1" ||
            ModelNumbers is null || ModelNumbers.Length is < 1 or > 16 ||
            ModelNumbers.Any(model => string.IsNullOrWhiteSpace(model) || model.Length > 64) ||
            !double.IsFinite(AspectRatio) || AspectRatio is < 0.1 or > 3 || Capabilities is null ||
            Controls is null || Controls.Length is < 1 or > 64 || Validation is null ||
            Validation.Any(pair => pair.Key is not ("windows" or "macos") ||
                pair.Value is not ("verified" or "candidate" or "research")) ||
            Controls.Any(control => control is null || !Regex.IsMatch(control.Id ?? "", "^[A-Za-z][A-Za-z0-9]{0,47}$") ||
                string.IsNullOrWhiteSpace(control.Label) || control.Label.Length > 32 || control.Usage == 0 ||
                !double.IsFinite(control.X) || !double.IsFinite(control.Y) || !double.IsFinite(control.Width) || !double.IsFinite(control.Height) ||
                control.X < 0 || control.Y < 0 || control.Width <= 0 || control.Height <= 0 ||
                control.X + control.Width > 1 || control.Y + control.Height > 1 || control.Gestures is null ||
                control.Gestures.Length is < 1 or > 3 || control.Gestures.Distinct().Count() != control.Gestures.Length ||
                control.Gestures.Any(gesture => gesture is not ("single" or "double" or "long" or "voice")) ||
                (control.Gestures.Contains("voice") && (control.Id != "Microphone" || control.Gestures.Length != 1)) ||
                (control.Id == "Microphone" && !control.Gestures.Contains("voice"))) ||
            Controls.Select(control => control.Id).Distinct(StringComparer.Ordinal).Count() != Controls.Length ||
            Controls.Select(control => control.Usage).Distinct().Count() != Controls.Length)
            throw new InvalidDataException("型号能力、按键或适配器配置无效。");
        if (Capabilities.Voice != Controls.Any(control => control.Gestures.Contains("voice")) ||
            (!Capabilities.Voice && (Capabilities.HoldToTalk || Capabilities.ToggleVoice)))
            throw new InvalidDataException("语音能力与按键定义不一致。");
        if (Capabilities.ToggleVoice || Capabilities.Touch || Capabilities.Battery ||
            (Capabilities.Voice && !Capabilities.HoldToTalk))
            throw new InvalidDataException("当前适配器尚不支持切换收音或触摸，请使用相应的新适配器。");
        if (Transport is { } transport && (transport.VendorId == 0 || transport.ProductId == 0 ||
            transport.VendorIdSource is not (1 or 2) || transport.AdvertisedNames is null ||
            transport.AdvertisedNames.Length is < 1 or > 16 || transport.AdvertisedNames.Any(name =>
                string.IsNullOrWhiteSpace(name) || name.Length > 80)))
            throw new InvalidDataException("传输层设备标识无效，请填写实际读取的 HID 标识和广播名。");
    }

    public string? ResolveArtwork(string directory)
    {
        if (Artwork is null) return null;
        if (Path.IsPathRooted(Artwork) || Artwork.Contains(':') || Artwork.Contains('\\') ||
            Artwork.Split('/').Any(part => part is ".." or "." or "") ||
            Path.GetExtension(Artwork).ToLowerInvariant() is not (".png" or ".jpg" or ".jpeg"))
            throw new InvalidDataException("型号图片必须是适配包内的 PNG 或 JPEG。");
        var root = Path.GetFullPath(directory) + Path.DirectorySeparatorChar;
        var path = Path.GetFullPath(Path.Combine(directory, Artwork));
        if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("图片路径超出适配包。");
        return path;
    }

    public bool MatchesModel(string model) => ModelNumbers.Any(candidate =>
        candidate.Equals(model.Trim(), StringComparison.OrdinalIgnoreCase));
}

public sealed record InstalledDeviceProfile(DeviceProfile Profile, string Directory);

public static class DeviceProfileCatalog
{
    public static IReadOnlyList<InstalledDeviceProfile> LoadDirectory(string directory)
    {
        if (!System.IO.Directory.Exists(directory)) return [];
        var paths = System.IO.Directory.GetFiles(directory, "profile.json", SearchOption.AllDirectories);
        if (paths.Length > 100) throw new InvalidDataException("型号包数量超过 100。");
        var profiles = paths.Order(StringComparer.Ordinal).Select(path =>
            new InstalledDeviceProfile(DeviceProfile.Load(path), Path.GetDirectoryName(path)!)).ToArray();
        if (profiles.Select(item => item.Profile.Id).Distinct(StringComparer.Ordinal).Count() != profiles.Length)
            throw new InvalidDataException("型号包 ID 重复。");
        return profiles;
    }
}
