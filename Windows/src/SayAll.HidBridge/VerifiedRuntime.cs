using System.Security.Cryptography;
using System.Text;
using SayAll.HidBridge.Contracts;

namespace SayAll.HidBridge;

internal sealed record VerifiedRuntimeFiles(
    string DriverPath,
    string GadgetPath,
    string ConfigPath,
    string ScriptPath);

internal static class VerifiedRuntime
{
    private const string RuntimeDirectoryName =
        @"RemoteMicRC003\hid-tap\17.15.3-x64-6fca4007b228";

    public static VerifiedRuntimeFiles Prepare(int port, string sessionToken)
    {
        var windowsDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var driverPath = Path.Combine(
            windowsDirectory,
            "System32",
            "drivers",
            "UMDF",
            Rc003DriverContract.DriverFileName);
        RequireSupportedDriver(driverPath);

        var programData = Environment.GetFolderPath(
            Environment.SpecialFolder.CommonApplicationData);
        var runtimeDirectory = Path.Combine(programData, RuntimeDirectoryName);
        var gadgetPath = Path.Combine(
            runtimeDirectory,
            GadgetRuntimeConfigBuilder.GadgetFileName);
        RequireFingerprint(
            gadgetPath,
            FridaGadgetContract.DllLength,
            FridaGadgetContract.DllSha256,
            "Frida Gadget");

        var configPath = Path.Combine(
            runtimeDirectory,
            GadgetRuntimeConfigBuilder.ConfigFileName);
        var scriptPath = Path.Combine(
            runtimeDirectory,
            GadgetRuntimeConfigBuilder.ScriptFileName);
        WriteUtf8(configPath, GadgetRuntimeConfigBuilder.Build());
        WriteUtf8(scriptPath, GadgetScriptBuilder.Build(port, sessionToken));

        RequireFingerprint(
            gadgetPath,
            FridaGadgetContract.DllLength,
            FridaGadgetContract.DllSha256,
            "Frida Gadget");

        return new VerifiedRuntimeFiles(driverPath, gadgetPath, configPath, scriptPath);
    }

    private static void RequireSupportedDriver(string path)
    {
        var file = new FileInfo(path);
        if (!file.Exists || file.Length != Rc003DriverContract.DriverFileLength)
        {
            throw new InvalidOperationException($"Unsupported HidOverGatt driver file: {path}");
        }

        using var stream = file.OpenRead();
        var hash = Convert.ToHexString(SHA256.HashData(stream));
        if (!Rc003DriverContract.IsSupported(file.Name, file.Length, hash))
        {
            throw new InvalidOperationException(
                $"Unsupported HidOverGatt driver fingerprint: {hash}");
        }
    }

    private static void RequireFingerprint(
        string path,
        long expectedLength,
        string expectedSha256,
        string label)
    {
        var file = new FileInfo(path);
        if (!file.Exists || file.Length != expectedLength)
        {
            throw new InvalidOperationException($"Unsupported {label} file: {path}");
        }

        using var stream = file.OpenRead();
        var hash = Convert.ToHexString(SHA256.HashData(stream));
        if (!string.Equals(hash, expectedSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Unsupported {label} fingerprint: {hash}");
        }
    }

    private static void WriteUtf8(string path, string content)
    {
        var temporaryPath = $"{path}.{Environment.ProcessId}.tmp";
        try
        {
            File.WriteAllText(
                temporaryPath,
                content,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            File.Delete(temporaryPath);
        }
    }
}
