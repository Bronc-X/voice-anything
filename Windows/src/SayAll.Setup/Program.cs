using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Windows;
using SayAll.HidBridge.Contracts;
using Microsoft.Win32;

[assembly: InternalsVisibleTo("SayAll.Windows.IntegrationTests")]

namespace SayAll.Setup;

internal static class Program
{
    private static readonly string InstallRoot = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
        HidBridgeServiceContract.ProductName);
    private static readonly string InstallLogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        HidBridgeServiceContract.ProductName,
        "Setup",
        "install.log");

    [STAThread]
    public static int Main()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(InstallLogPath)!);
            Log("开始安装 VoiceAnything");
            Install();
            MessageBox.Show(
                "VoiceAnything 已安装完成。后台按键组件会随 Windows 自动启动，今后打开应用不会再弹出命令行或管理员授权。",
                "VoiceAnything 安装完成",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            LaunchInstalledApp();
            return 0;
        }
        catch (Exception exception)
        {
            Log($"安装失败：{exception}");
            MessageBox.Show(
                $"VoiceAnything 安装失败：\n\n{exception.Message}\n\n安装日志：{InstallLogPath}",
                "VoiceAnything 安装失败",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return 1;
        }
    }

    private static void Install()
    {
        var payloadRoot = Path.Combine(AppContext.BaseDirectory, "Payload");
        var appSource = Path.Combine(payloadRoot, "App");
        var bridgeSource = Path.Combine(payloadRoot, "HidBridge");
        RequireDirectory(appSource, "应用文件");
        RequireDirectory(bridgeSource, "后台按键组件");
        var uninstallSource = Path.Combine(AppContext.BaseDirectory, "Uninstall.ps1");
        RequireFile(uninstallSource, "卸载程序");

        StopServiceIfPresent(HidBridgeServiceContract.ServiceName);
        StopServiceIfPresent(HidBridgeServiceContract.LegacyServiceName);
        StopServiceIfPresent("VibeControlHidBridge");
        StopOwnedProcesses();

        var appDestination = Path.Combine(InstallRoot, "App");
        var bridgeDestination = Path.Combine(InstallRoot, "HidBridge");
        CopyDirectory(appSource, appDestination);
        CopyDirectory(bridgeSource, bridgeDestination);
        InstallVerifiedGadget(bridgeSource);
        PrepareSelectionDirectory();

        var bridgeExecutable = Path.Combine(bridgeDestination, "VoiceAnything.HidBridge.exe");
        var appExecutable = Path.Combine(appDestination, "VoiceAnything.exe");
        RequireFile(bridgeExecutable, "后台按键组件主程序");
        RequireFile(appExecutable, "VoiceAnything 主程序");

        DeleteServiceIfPresent(HidBridgeServiceContract.ServiceName);
        DeleteServiceIfPresent(HidBridgeServiceContract.LegacyServiceName);
        DeleteServiceIfPresent("VibeControlHidBridge");
        RunServiceControl(
            "create",
            HidBridgeServiceContract.ServiceName,
            "binPath=",
            $"\"{bridgeExecutable}\" --service",
            "start=",
            "auto",
            "DisplayName=",
            HidBridgeServiceContract.DisplayName);
        RunServiceControl(
            "description",
            HidBridgeServiceContract.ServiceName,
            "Reads authenticated RC003MS HID reports for VoiceAnything.");
        RunServiceControl("sidtype", HidBridgeServiceContract.ServiceName, "unrestricted");
        RunServiceControl(
            "failure",
            HidBridgeServiceContract.ServiceName,
            "reset=",
            "86400",
            "actions=",
            "restart/5000/restart/15000/restart/60000");
        RunServiceControl("start", HidBridgeServiceContract.ServiceName);
        CreateStartMenuShortcut(appExecutable);
        File.WriteAllText(
            Path.Combine(InstallRoot, "install-receipt.txt"),
            $"Installed={DateTimeOffset.Now:O}{Environment.NewLine}" +
            $"Service={HidBridgeServiceContract.ServiceName}{Environment.NewLine}" +
            $"Bridge={bridgeExecutable}{Environment.NewLine}" +
            $"App={appExecutable}{Environment.NewLine}",
            new UTF8Encoding(false));
        File.Copy(uninstallSource, Path.Combine(InstallRoot, "Uninstall.ps1"), overwrite: true);
        RegisterUninstaller(appExecutable);
        Log("VoiceAnything 主程序和后台服务已安装");
    }

    private static void RegisterUninstaller(string appExecutable)
    {
        using var key = Registry.LocalMachine.CreateSubKey(
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\VoiceAnything")
            ?? throw new IOException("无法注册卸载入口。");
        var powershell = Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
        key.SetValue("DisplayName", "Voice Anything");
        key.SetValue("DisplayVersion", "0.1.0-dev");
        key.SetValue("Publisher", "Voice Anything contributors");
        key.SetValue("InstallLocation", InstallRoot);
        key.SetValue("DisplayIcon", appExecutable);
        key.SetValue("UninstallString", $"\"{powershell}\" -NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -File \"{Path.Combine(InstallRoot, "Uninstall.ps1")}\"");
        key.SetValue("NoModify", 1, RegistryValueKind.DWord);
        key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
    }

    private static void InstallVerifiedGadget(string bridgeSource)
    {
        var source = Path.Combine(bridgeSource, "RemoteMicRC003HidTap.dll");
        RequireFile(source, "固定版本 RC003MS HID 运行库");
        var sourceInfo = new FileInfo(source);
        using var sourceStream = sourceInfo.OpenRead();
        var sourceHash = Convert.ToHexString(SHA256.HashData(sourceStream));
        if (!FridaGadgetContract.IsSupported(sourceInfo.Length, sourceHash))
        {
            throw new InvalidDataException(
                $"RC003MS HID 运行库指纹不匹配：{sourceHash}");
        }

        var destinationDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "RemoteMicRC003",
            "hid-tap",
            "17.15.3-x64-6fca4007b228");
        Directory.CreateDirectory(destinationDirectory);
        var destination = Path.Combine(
            destinationDirectory,
            "RemoteMicRC003HidTap.dll");
        var copied = PinnedFileInstaller.InstallOrReuse(
            source,
            destination,
            FridaGadgetContract.DllLength,
            FridaGadgetContract.DllSha256);
        Log(copied
            ? $"已验证并安装固定 HID 运行库：{sourceHash}"
            : $"已验证并复用正在运行的固定 HID 运行库：{sourceHash}");
    }

    private static void PrepareSelectionDirectory()
    {
        // Users may select hardware; executable and driver directories remain administrator-owned.
        var directory = Directory.CreateDirectory(Path.GetDirectoryName(DeviceSelection.SelectionPath)!);
        var security = directory.GetAccessControl();
        security.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null), FileSystemRights.Modify,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None, AccessControlType.Allow));
        directory.SetAccessControl(security);
    }

    private static void StopOwnedProcesses()
    {
        var installedBridge = Path.Combine(
            InstallRoot,
            "HidBridge",
            "VoiceAnything.HidBridge.exe");
        var installedApp = Path.Combine(InstallRoot, "App", "VoiceAnything.exe");
        StopProcessesAtPath(
            "VoiceAnything.HidBridge",
            installedBridge,
            "VoiceAnything.HidBridge.exe",
            includeOtherProductCopies: true);
        StopProcessesAtPath(
            "VoiceAnything",
            installedApp,
            "VoiceAnything.exe",
            includeOtherProductCopies: false);

        var legacyRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "SayAll");
        StopProcessesAtPath(
            "SayAll.HidBridge",
            Path.Combine(legacyRoot, "HidBridge", "SayAll.HidBridge.exe"),
            "SayAll.HidBridge.exe",
            includeOtherProductCopies: true);
        StopProcessesAtPath(
            "SayAll.Windows",
            Path.Combine(legacyRoot, "App", "SayAll.Windows.exe"),
            "SayAll.Windows.exe",
            includeOtherProductCopies: false);
    }

    private static void StopProcessesAtPath(
        string processName,
        string installedPath,
        string ownedExecutableName,
        bool includeOtherProductCopies)
    {
        foreach (var process in Process.GetProcessesByName(processName))
        {
            using (process)
            {
                string? executablePath;
                try
                {
                    executablePath = process.MainModule?.FileName;
                }
                catch
                {
                    executablePath = null;
                }

                var isInstalledCopy = executablePath is not null &&
                    executablePath.Equals(installedPath, StringComparison.OrdinalIgnoreCase);
                var isOwnedBridgeCopy = includeOtherProductCopies &&
                    executablePath is not null &&
                    Path.GetFileName(executablePath).Equals(
                        ownedExecutableName,
                        StringComparison.OrdinalIgnoreCase);
                if (!isInstalledCopy && !isOwnedBridgeCopy)
                {
                    continue;
                }

                Log($"正在停止旧 VoiceAnything/SayAll 进程：PID {process.Id} · {executablePath}");
                process.Kill(entireProcessTree: false);
                process.WaitForExit(5_000);
            }
        }
    }

    private static void StopServiceIfPresent(string serviceName)
    {
        if (ServiceExists(serviceName))
        {
            RunServiceControl(allowFailure: true, "stop", serviceName);
            Thread.Sleep(1_000);
        }
    }

    private static void DeleteServiceIfPresent(string serviceName)
    {
        if (ServiceExists(serviceName))
        {
            RunServiceControl("delete", serviceName);
            Thread.Sleep(500);
        }
    }

    private static bool ServiceExists(string serviceName)
    {
        return RunServiceControl(
            allowFailure: true,
            "query",
            serviceName) == 0;
    }

    private static int RunServiceControl(params string[] arguments)
    {
        return RunServiceControl(allowFailure: false, arguments);
    }

    private static int RunServiceControl(bool allowFailure, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = Path.Combine(Environment.SystemDirectory, "sc.exe"),
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("无法启动 Windows 服务管理器。" );
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        Log($"sc {string.Join(' ', arguments)} → {process.ExitCode} {output.Trim()} {error.Trim()}" );
        if (!allowFailure && process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Windows 服务配置失败（{process.ExitCode}）：{error.Trim()} {output.Trim()}" );
        }

        return process.ExitCode;
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(source, file);
            var target = Path.Combine(destination, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            LockedFileCopy.CopyWithRetry(
                file,
                target,
                overwrite: true,
                timeout: TimeSpan.FromSeconds(12));
        }
    }

    private static void CreateStartMenuShortcut(string appExecutable)
    {
        var startMenu = Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu);
        var programsDirectory = Path.Combine(startMenu, "Programs");
        var shortcutPath = Path.Combine(programsDirectory, "VoiceAnything.lnk");
        var legacyShortcutPath = Path.Combine(programsDirectory, "SayAll.lnk");
        if (File.Exists(legacyShortcutPath))
        {
            File.Delete(legacyShortcutPath);
        }
        var shellType = Type.GetTypeFromProgID("WScript.Shell")
            ?? throw new InvalidOperationException("Windows 快捷方式服务不可用。" );
        var shell = Activator.CreateInstance(shellType)
            ?? throw new InvalidOperationException("无法创建 Windows 快捷方式服务。" );
        try
        {
            var shortcut = shellType.InvokeMember(
                "CreateShortcut",
                BindingFlags.InvokeMethod,
                null,
                shell,
                [shortcutPath]);
            var shortcutType = shortcut!.GetType();
            shortcutType.InvokeMember(
                "TargetPath",
                BindingFlags.SetProperty,
                null,
                shortcut,
                [appExecutable]);
            shortcutType.InvokeMember(
                "WorkingDirectory",
                BindingFlags.SetProperty,
                null,
                shortcut,
                [Path.GetDirectoryName(appExecutable)!]);
            shortcutType.InvokeMember(
                "Save",
                BindingFlags.InvokeMethod,
                null,
                shortcut,
                null);
        }
        finally
        {
            if (System.Runtime.InteropServices.Marshal.IsComObject(shell))
            {
                System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell);
            }
        }
    }

    private static void LaunchInstalledApp()
    {
        var appExecutable = Path.Combine(InstallRoot, "App", "VoiceAnything.exe");
        Process.Start(new ProcessStartInfo(appExecutable)
        {
            UseShellExecute = true,
            WorkingDirectory = Path.GetDirectoryName(appExecutable),
        });
    }

    private static void RequireDirectory(string path, string label)
    {
        if (!Directory.Exists(path))
        {
            throw new DirectoryNotFoundException($"安装包缺少{label}：{path}");
        }
    }

    private static void RequireFile(string path, string label)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"安装包缺少{label}。", path);
        }
    }

    private static void Log(string message)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(InstallLogPath)!);
        File.AppendAllText(
            InstallLogPath,
            $"{DateTimeOffset.Now:O} {message}{Environment.NewLine}",
            new UTF8Encoding(false));
    }
}

internal static class PinnedFileInstaller
{
    public static bool InstallOrReuse(
        string source,
        string destination,
        long expectedLength,
        string expectedSha256)
    {
        if (!HasFingerprint(source, expectedLength, expectedSha256))
        {
            throw new InvalidDataException($"固定运行库源文件指纹不匹配：{source}");
        }

        if (HasFingerprint(destination, expectedLength, expectedSha256))
        {
            return false;
        }

        File.Copy(source, destination, overwrite: true);
        if (!HasFingerprint(destination, expectedLength, expectedSha256))
        {
            throw new IOException($"固定运行库复制后校验失败：{destination}");
        }

        return true;
    }

    private static bool HasFingerprint(
        string path,
        long expectedLength,
        string expectedSha256)
    {
        var file = new FileInfo(path);
        if (!file.Exists || file.Length != expectedLength)
        {
            return false;
        }

        using var stream = file.OpenRead();
        var actualSha256 = Convert.ToHexString(SHA256.HashData(stream));
        return string.Equals(
            actualSha256,
            expectedSha256,
            StringComparison.OrdinalIgnoreCase);
    }
}

internal static class LockedFileCopy
{
    private static readonly TimeSpan RetryInterval = TimeSpan.FromMilliseconds(100);

    public static void CopyWithRetry(
        string source,
        string destination,
        bool overwrite,
        TimeSpan timeout)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(timeout, TimeSpan.Zero);

        var stopwatch = Stopwatch.StartNew();
        while (true)
        {
            try
            {
                File.Copy(source, destination, overwrite);
                return;
            }
            catch (IOException) when (stopwatch.Elapsed < timeout)
            {
                Thread.Sleep(RetryInterval);
            }
        }
    }
}
