using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SayAll.HidBridge.Contracts;

public sealed record SelectedRemote(string ProfileId, string BluetoothAddress);

public static class DeviceSelection
{
    public static string SelectionPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "VoiceAnything", "Selection", "device.json");

    public static bool ValidAddress(string? address) => address is not null &&
        Regex.IsMatch(address, "^[0-9A-Fa-f]{12}$") && address != "000000000000";

    public static string? HidAddress(string instanceId)
    {
        if (!Rc003DriverContract.IsSupportedDeviceInstanceId(instanceId)) return null;
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
        if (remote is null || !ValidAddress(remote.BluetoothAddress) ||
            !Regex.IsMatch(remote.ProfileId ?? "", "^[a-z0-9][a-z0-9-]{2,63}$"))
            throw new InvalidDataException("Device selection is invalid.");
        return remote;
    }

    public static void Save(SelectedRemote remote, string? path = null)
    {
        if (!ValidAddress(remote.BluetoothAddress) || !Regex.IsMatch(remote.ProfileId, "^[a-z0-9][a-z0-9-]{2,63}$"))
            throw new ArgumentException("Invalid device selection.");
        path ??= SelectionPath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(temp, JsonSerializer.Serialize(remote)); File.Move(temp, path, true); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
