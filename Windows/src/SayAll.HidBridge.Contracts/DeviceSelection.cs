using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SayAll.HidBridge.Contracts;

public sealed record SelectedRemote(string ProfileId, string BluetoothAddress,
    string HidHardwareToken = DeviceSelection.DefaultHardwareToken);

public static class DeviceSelection
{
    public const string DefaultHardwareToken = "Dev_VID&012717_PID&32b8_REV&00a4_";
    public static bool ValidHardwareToken(string? token) => token is not null &&
        Regex.IsMatch(token, "^Dev_VID&0[12][0-9A-Fa-f]{4}_PID&[0-9A-Fa-f]{4}_REV&[0-9A-Fa-f]{4}_$", RegexOptions.IgnoreCase);
    public static string SelectionPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "VoiceAnything", "Selection", "device.json");

    public static bool ValidAddress(string? address) => address is not null &&
        Regex.IsMatch(address, "^[0-9A-Fa-f]{12}$") && address != "000000000000";

    public static string? HidAddress(string instanceId, string hardwareToken = DefaultHardwareToken)
    {
        if (!ValidHardwareToken(hardwareToken) || !instanceId.StartsWith(
                @"BTHLEDevice\{00001812-0000-1000-8000-00805F9B34FB}_" + hardwareToken,
                StringComparison.OrdinalIgnoreCase)) return null;
        var match = Regex.Match(instanceId, @"_([0-9a-f]{12})\\", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value.ToUpperInvariant() : null;
    }

    public static bool MatchesPhysical(string instanceId, string address)
    {
        if (!ValidAddress(address)) return false;
        return instanceId.StartsWith($@"BTHLE\DEV_{address}\", StringComparison.OrdinalIgnoreCase);
    }

    public static string Key(string address)
    {
        if (!ValidAddress(address)) throw new ArgumentException("Invalid Bluetooth identity.");
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(address.ToUpperInvariant())));
    }

    public static SelectedRemote? Read(string? path = null)
    {
        path ??= SelectionPath;
        if (!File.Exists(path)) return null;
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        if (stream.Length > 4096) throw new InvalidDataException("Device selection is too large.");
        var remote = JsonSerializer.Deserialize<SelectedRemote>(stream);
        if (remote is null || !ValidAddress(remote.BluetoothAddress) || !ValidHardwareToken(remote.HidHardwareToken) ||
            !Regex.IsMatch(remote.ProfileId ?? "", "^[a-z0-9][a-z0-9-]{2,63}$"))
            throw new InvalidDataException("Device selection is invalid.");
        return remote;
    }

    public static void Save(SelectedRemote remote, string? path = null)
    {
        if (!ValidAddress(remote.BluetoothAddress) || !ValidHardwareToken(remote.HidHardwareToken) || !Regex.IsMatch(remote.ProfileId, "^[a-z0-9][a-z0-9-]{2,63}$"))
            throw new ArgumentException("Invalid device selection.");
        path ??= SelectionPath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(temp, JsonSerializer.Serialize(remote)); File.Move(temp, path, true); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
