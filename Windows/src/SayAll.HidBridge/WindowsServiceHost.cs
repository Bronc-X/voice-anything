using System.ComponentModel;
using System.Runtime.InteropServices;
using SayAll.HidBridge.Contracts;

namespace SayAll.HidBridge;

internal static class WindowsServiceHost
{
    private const uint ServiceWin32OwnProcess = 0x00000010;
    private const uint ServiceStartPending = 0x00000002;
    private const uint ServiceStopPending = 0x00000003;
    private const uint ServiceRunning = 0x00000004;
    private const uint ServiceStopped = 0x00000001;
    private const uint ServiceAcceptStop = 0x00000001;
    private const uint ServiceAcceptShutdown = 0x00000004;
    private const uint ServiceControlStop = 0x00000001;
    private const uint ServiceControlShutdown = 0x00000005;

    private static readonly ServiceMainCallback ServiceMainDelegate = ServiceMain;
    private static readonly ServiceControlHandler ControlHandlerDelegate = HandleControl;
    private static Func<CancellationToken, Task>? _worker;
    private static CancellationTokenSource? _stopping;
    private static IntPtr _statusHandle;
    private static uint _checkpoint;

    public static int Run(Func<CancellationToken, Task> worker)
    {
        _worker = worker ?? throw new ArgumentNullException(nameof(worker));
        var table = new[]
        {
            new ServiceTableEntry
            {
                ServiceName = HidBridgeServiceContract.ServiceName,
                ServiceMain = ServiceMainDelegate,
            },
            new ServiceTableEntry(),
        };
        if (!StartServiceCtrlDispatcher(table))
        {
            throw new Win32Exception(
                Marshal.GetLastWin32Error(),
                "Could not connect VoiceAnything HID bridge to the Windows service manager.");
        }

        return 0;
    }

    private static void ServiceMain(uint argumentCount, IntPtr arguments)
    {
        _statusHandle = RegisterServiceCtrlHandler(
            HidBridgeServiceContract.ServiceName,
            ControlHandlerDelegate);
        if (_statusHandle == IntPtr.Zero)
        {
            return;
        }

        _stopping = new CancellationTokenSource();
        PublishStatus(ServiceStartPending, acceptedControls: 0, waitHint: 10_000);
        PublishStatus(
            ServiceRunning,
            ServiceAcceptStop | ServiceAcceptShutdown,
            waitHint: 0);

        uint exitCode = 0;
        try
        {
            _worker!(_stopping.Token).GetAwaiter().GetResult();
        }
        catch
        {
            exitCode = 1;
        }
        finally
        {
            PublishStatus(ServiceStopped, acceptedControls: 0, waitHint: 0, exitCode);
            _stopping.Dispose();
            _stopping = null;
        }
    }

    private static void HandleControl(uint controlCode)
    {
        if (controlCode is not (ServiceControlStop or ServiceControlShutdown) ||
            _stopping is null)
        {
            return;
        }

        PublishStatus(ServiceStopPending, acceptedControls: 0, waitHint: 10_000);
        _stopping.Cancel();
    }

    private static void PublishStatus(
        uint currentState,
        uint acceptedControls,
        uint waitHint,
        uint exitCode = 0)
    {
        var status = new ServiceStatus
        {
            ServiceType = ServiceWin32OwnProcess,
            CurrentState = currentState,
            ControlsAccepted = acceptedControls,
            Win32ExitCode = exitCode,
            CheckPoint = currentState is ServiceStartPending or ServiceStopPending
                ? ++_checkpoint
                : 0,
            WaitHint = waitHint,
        };
        _ = SetServiceStatus(_statusHandle, ref status);
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void ServiceMainCallback(uint argumentCount, IntPtr arguments);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void ServiceControlHandler(uint controlCode);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ServiceTableEntry
    {
        [MarshalAs(UnmanagedType.LPWStr)]
        public string? ServiceName;

        public ServiceMainCallback? ServiceMain;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ServiceStatus
    {
        public uint ServiceType;
        public uint CurrentState;
        public uint ControlsAccepted;
        public uint Win32ExitCode;
        public uint ServiceSpecificExitCode;
        public uint CheckPoint;
        public uint WaitHint;
    }

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool StartServiceCtrlDispatcher(ServiceTableEntry[] serviceTable);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr RegisterServiceCtrlHandler(
        string serviceName,
        ServiceControlHandler handler);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetServiceStatus(
        IntPtr statusHandle,
        ref ServiceStatus serviceStatus);
}
