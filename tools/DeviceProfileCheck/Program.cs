using System.Text.Json;
using SayAll.Core.Devices;

if (args.Length != 1)
{
    Console.Error.WriteLine("Usage: DeviceProfileCheck <folder containing profile.json>");
    return 2;
}
try
{
    var path = Path.Combine(Path.GetFullPath(args[0]), "profile.json");
    var profile = DeviceProfile.Load(path);
    Console.WriteLine($"VALID {profile.Id}: {profile.Controls.Length} controls, adapter {profile.Adapter}");
    Console.WriteLine($"DIS: {string.Join(", ", profile.ModelNumbers)}");
    Console.WriteLine($"HID: {profile.Transport?.HardwareToken ?? "legacy RC003 identity"}");
    Console.WriteLine("This validates configuration. Hardware compatibility still requires real-device acceptance.");
    return 0;
}
catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
{
    Console.Error.WriteLine("INVALID: " + error.Message);
    return 1;
}
