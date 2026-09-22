namespace SayAll.HidBridge.Contracts;

public static class Rc003DriverContract
{
    private const string HidServiceInstancePrefix =
        @"BTHLEDevice\{00001812-0000-1000-8000-00805F9B34FB}_";
    private const string Rc003HardwareToken =
        "Dev_VID&012717_PID&32b8_REV&00a4_";

    public const string DriverFileName =
        "Microsoft.Bluetooth.Profiles.HidOverGatt.dll";
    public const long DriverFileLength = 233_472;
    public const string DriverSha256 =
        "D628C3A9C6B34E51D79F91C204A787AD6A598F863BF24395E612FBFD3BBE0B87";
    public const string CurrentWindowsDriverSha256 =
        "372C3628E3366C18152199EB8FB554D27BB4F02D1356B1F514F711328DF4A8D6";
    public const int CapturePointRva = 0x15980;
    public const int ReportArrivedRva = 0x20720;

    private static readonly byte[] CapturePointBytes =
        Convert.FromHexString("B918000000E826CAFEFF");
    private static readonly byte[] ReportArrivedBytes =
        Convert.FromHexString("4C894424188854241055");

    public static bool IsSupported(string fileName, long length, string sha256)
    {
        return string.Equals(fileName, DriverFileName, StringComparison.OrdinalIgnoreCase) &&
            length == DriverFileLength &&
            (string.Equals(sha256, DriverSha256, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(
                    sha256,
                    CurrentWindowsDriverSha256,
                    StringComparison.OrdinalIgnoreCase));
    }

    public static bool IsSupportedDeviceInstanceId(string instanceId)
    {
        return instanceId.StartsWith(
                HidServiceInstancePrefix,
                StringComparison.OrdinalIgnoreCase) &&
            instanceId.Contains(Rc003HardwareToken, StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsSupportedHostProcessName(string processName)
    {
        return string.Equals(processName, "WUDFHost.exe", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsSupportedCapturePoint(int rva, byte[] machineCode)
    {
        ArgumentNullException.ThrowIfNull(machineCode);
        return rva == CapturePointRva && machineCode.AsSpan().SequenceEqual(CapturePointBytes);
    }

    public static bool IsSupportedReportArrivedPoint(int rva, byte[] machineCode)
    {
        ArgumentNullException.ThrowIfNull(machineCode);
        return rva == ReportArrivedRva &&
            machineCode.AsSpan().SequenceEqual(ReportArrivedBytes);
    }
}
