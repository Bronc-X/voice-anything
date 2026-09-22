using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;
using SayAll.HidBridge.Contracts;

namespace SayAll.HidBridge;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        try
        {
            if (!OperatingSystem.IsWindows())
            {
                throw new PlatformNotSupportedException("VoiceAnything HID bridge requires Windows.");
            }

            var options = BridgeOptions.Parse(args);
            if (options.ServiceMode)
            {
                return WindowsServiceHost.Run(RunServiceAsync);
            }

            using var logger = BridgeEventLogger.Create(options.LogPath, append: false);
            await RunBridgeSessionAsync(options.ProbeSeconds, logger.Log, CancellationToken.None);
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(JsonSerializer.Serialize(new
            {
                timestamp = DateTimeOffset.Now,
                @event = "bridge_error",
                detail = exception.Message,
            }));
            return 1;
        }
    }

    private static async Task RunServiceAsync(CancellationToken cancellationToken)
    {
        using var logger = BridgeEventLogger.Create(
            HidBridgeServiceContract.SharedEventLogPath,
            append: true);
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await RunBridgeSessionAsync(null, logger.Log, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.Log("bridge_error", exception.Message);
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private static async Task RunBridgeSessionAsync(
        int? probeSeconds,
        Action<string, object?, string?> publish,
        CancellationToken cancellationToken)
    {
        var selection = DeviceSelection.Read() ??
            throw new InvalidOperationException("Choose a paired remote in Voice Anything first.");
        var deviceKey = DeviceSelection.Key(selection.BluetoothAddress);
        void log(string name, object? detail) => publish(name, detail, deviceKey);
        using var selectionCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cancellationToken = selectionCancellation.Token;
        var monitor = MonitorSelectionAsync(selection, selectionCancellation);
        try
        {
        log("bridge_starting", null);
        var target = Rc003RegistryLocator.FindSingleSupportedHost() ??
            throw new InvalidOperationException(
                "No unique connected RC003MS HID host was found. Reconnect MI RC and try again.");
        log("remote_host_found", new { target.HostPid });

        WindowsInjection.EnableDebugPrivilege();
        using var process = Process.GetProcessById(target.HostPid);
        var targetProcessName = process.ProcessName + ".exe";
        if (!Rc003DriverContract.IsSupportedHostProcessName(targetProcessName))
        {
            throw new InvalidOperationException(
                $"Refusing unexpected host process: {targetProcessName}");
        }
        log("remote_host_verified", new { target.HostPid, process = targetProcessName });

        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start(backlog: 1);
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var sessionToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var runtime = VerifiedRuntime.Prepare(port, sessionToken);
        log("runtime_verified", new { runtime.DriverPath, runtime.GadgetPath, port });

        TcpClient? acceptedClient = null;
        for (var attempt = 1;
             attempt <= HidBridgeStartupPolicy.MaximumInjectionAttempts;
             attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (DeviceSelection.Read() != selection ||
                DeviceSelection.HidAddress(target.InstanceId, selection.HidHardwareToken) != selection.BluetoothAddress.ToUpperInvariant())
                throw new InvalidOperationException("The selected remote changed; reconnecting.");
            WindowsInjection.InjectLibrary(target.HostPid, runtime.GadgetPath);
            log("gadget_injected", new { target.HostPid, attempt });

            using var connectTimeout = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);
            connectTimeout.CancelAfter(
                TimeSpan.FromSeconds(HidBridgeStartupPolicy.GadgetConnectTimeoutSeconds));
            try
            {
                acceptedClient = await listener.AcceptTcpClientAsync(connectTimeout.Token);
                break;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                log("gadget_connect_timeout", new { target.HostPid, attempt });
                if (!HidBridgeStartupPolicy.ShouldRestartDeviceAfterTimeout(attempt))
                {
                    throw new TimeoutException(
                        "RC003MS button bridge did not reconnect after one controlled device restart.");
                }

                log("device_restart_started", null);
                await RestartDeviceAsync(target.InstanceId, cancellationToken);
                target = await WaitForRestartedHostAsync(target.HostPid, cancellationToken);
                log("device_restart_completed", new { target.HostPid });
            }
        }

        using var client = acceptedClient ??
            throw new TimeoutException("RC003MS button bridge did not connect.");
        log("gadget_connected", new { remote = client.Client.RemoteEndPoint?.ToString() });

        using var reader = new StreamReader(client.GetStream());
        var hookReady = false;
        using var sessionCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken);
        if (probeSeconds is not null)
        {
            sessionCancellation.CancelAfter(TimeSpan.FromSeconds(probeSeconds.Value));
        }

        try
        {
            while (!sessionCancellation.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(sessionCancellation.Token);
                if (line is null)
                {
                    break;
                }

                var readiness = HidBridgeMessageCodec.ParseReadyLine(line, sessionToken);
                if (readiness is not null)
                {
                    hookReady = readiness.Value;
                    log(hookReady ? "hook_ready" : "hook_loading", null);
                    continue;
                }

                var report = HidBridgeMessageCodec.ParseGadgetLine(line, sessionToken);
                if (report is not null && hookReady)
                {
                    log("hid_report", new
                    {
                        report_id = report.ReportId,
                        payload = Convert.ToHexString(report.Payload),
                    });
                    continue;
                }

                var probe = HidBridgeMessageCodec.ParseProbeLine(line, sessionToken);
                if (probe is not null && hookReady)
                {
                    log("hook_probe", new
                    {
                        source = probe.Source,
                        report_id = probe.ReportId,
                        payload_length = probe.PayloadLength,
                        reason = probe.Reason,
                    });
                }
            }
        }
        catch (OperationCanceledException) when (sessionCancellation.IsCancellationRequested)
        {
        }

        log("bridge_stopped", null);
        }
        finally
        {
            selectionCancellation.Cancel();
            await monitor;
        }
    }

    private static async Task MonitorSelectionAsync(SelectedRemote expected, CancellationTokenSource cancellation)
    {
        try
        {
            while (!cancellation.IsCancellationRequested)
            {
                await Task.Delay(500, cancellation.Token);
                if (DeviceSelection.Read() != expected) { cancellation.Cancel(); return; }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        { cancellation.Cancel(); }
    }

    private static async Task RestartDeviceAsync(
        string instanceId,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = Path.Combine(Environment.SystemDirectory, "pnputil.exe"),
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        startInfo.ArgumentList.Add("/restart-device");
        startInfo.ArgumentList.Add(instanceId);

        using var process = Process.Start(startInfo) ??
            throw new InvalidOperationException("Could not start pnputil.exe.");
        await process.WaitForExitAsync(cancellationToken);
        if (process.ExitCode != 0)
        {
            var detail = await process.StandardError.ReadToEndAsync(cancellationToken);
            throw new InvalidOperationException(
                $"RC003MS device restart failed ({process.ExitCode}): {detail.Trim()}");
        }
    }

    private static async Task<Rc003HostCandidate> WaitForRestartedHostAsync(
        int previousHostPid,
        CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(15);
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var candidate = Rc003RegistryLocator.FindSingleSupportedHost();
            if (candidate is not null &&
                candidate.HostPid != previousHostPid &&
                IsRunningProcess(candidate.HostPid))
            {
                return candidate;
            }

            await Task.Delay(250, cancellationToken);
        }

        throw new TimeoutException("Windows did not reload the RC003MS HID host within 15 seconds.");
    }

    private static bool IsRunningProcess(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}

internal sealed record BridgeOptions(int? ProbeSeconds, string? LogPath, bool ServiceMode)
{
    public static BridgeOptions Parse(string[] args)
    {
        int? probeSeconds = 120;
        string? logPath = null;
        var serviceMode = false;
        for (var index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--service":
                    serviceMode = true;
                    probeSeconds = null;
                    break;
                case "--probe-seconds" when index + 1 < args.Length:
                    if (!int.TryParse(args[++index], out var parsedProbeSeconds) ||
                        parsedProbeSeconds is < 5 or > 600)
                    {
                        throw new ArgumentException("--probe-seconds must be between 5 and 600.");
                    }
                    probeSeconds = parsedProbeSeconds;
                    break;
                case "--log" when index + 1 < args.Length:
                    logPath = args[++index];
                    break;
                default:
                    throw new ArgumentException($"Unknown or incomplete argument: {args[index]}");
            }
        }

        if (serviceMode && (logPath is not null || probeSeconds is not null))
        {
            throw new ArgumentException("--service cannot be combined with probe arguments.");
        }

        return new BridgeOptions(probeSeconds, logPath, serviceMode);
    }
}

internal sealed class BridgeEventLogger : IDisposable
{
    private const long MaximumLogBytes = 5 * 1024 * 1024;
    private readonly TextWriter? _writer;

    private BridgeEventLogger(TextWriter? writer)
    {
        _writer = writer;
    }

    public static BridgeEventLogger Create(string? path, bool append)
    {
        if (path is null)
        {
            return new BridgeEventLogger(null);
        }

        var fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        if (append && new FileInfo(fullPath) is { Exists: true, Length: > MaximumLogBytes })
        {
            File.Move(fullPath, fullPath + ".previous", overwrite: true);
        }

        return new BridgeEventLogger(TextWriter.Synchronized(new StreamWriter(
            fullPath,
            append) { AutoFlush = true }));
    }

    public void Log(string eventName, object? detail = null, string? deviceKey = null)
    {
        var line = JsonSerializer.Serialize(new
        {
            timestamp = DateTimeOffset.Now,
            @event = eventName,
            device_key = deviceKey,
            detail,
        });
        Console.WriteLine(line);
        _writer?.WriteLine(line);
    }

    public void Dispose()
    {
        _writer?.Dispose();
    }
}
