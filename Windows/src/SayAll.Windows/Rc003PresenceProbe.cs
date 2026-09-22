using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;
using SayAll.HidBridge.Contracts;

namespace SayAll.Windows;

internal sealed record Rc003PresenceState(
    bool BluetoothConnected,
    bool HidServicePresent,
    int? HidHostPid);

internal static class Rc003PresenceProbe
{
    private const uint DigcfPresent = 0x00000002;
    private const uint DigcfAllClasses = 0x00000004;
    private const int ErrorNoMoreItems = 259;
    private const string BluetoothLeDevicesKey =
        @"SYSTEM\CurrentControlSet\Enum\BTHLEDevice";

    public static bool IsConnected()
    {
        return GetState().BluetoothConnected;
    }

    public static Rc003PresenceState GetState()
    {
        var selection = DeviceSelection.Read();
        if (selection is null) return new(false, false, null);
        var bluetoothConnected = IsPhysicalDevicePresent(selection.BluetoothAddress);
        var (hidServicePresent, hostPid) = ReadHidServiceState(selection.BluetoothAddress);
        return new Rc003PresenceState(bluetoothConnected, hidServicePresent, hostPid);
    }

    private static bool IsPhysicalDevicePresent(string selectedAddress)
    {
        var deviceInfoSet = SetupDiGetClassDevs(
            IntPtr.Zero,
            null,
            IntPtr.Zero,
            DigcfPresent | DigcfAllClasses);
        if (deviceInfoSet == new IntPtr(-1))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        try
        {
            for (uint index = 0; ; index++)
            {
                var deviceInfo = new SpDevinfoData
                {
                    Size = (uint)Marshal.SizeOf<SpDevinfoData>(),
                };
                if (!SetupDiEnumDeviceInfo(deviceInfoSet, index, ref deviceInfo))
                {
                    var error = Marshal.GetLastWin32Error();
                    if (error == ErrorNoMoreItems)
                    {
                        return false;
                    }

                    throw new Win32Exception(error);
                }

                var instanceId = new StringBuilder(512);
                if (SetupDiGetDeviceInstanceId(
                        deviceInfoSet,
                        ref deviceInfo,
                        instanceId,
                        instanceId.Capacity,
                        out _) &&
                    DeviceSelection.MatchesPhysical(instanceId.ToString(), selectedAddress))
                {
                    return true;
                }
            }
        }
        finally
        {
            SetupDiDestroyDeviceInfoList(deviceInfoSet);
        }
    }

    private static (bool IsPresent, int? HostPid) ReadHidServiceState(string selectedAddress)
    {
        using var root = Registry.LocalMachine.OpenSubKey(BluetoothLeDevicesKey);
        if (root is null)
        {
            return (false, null);
        }

        var isPresent = false;
        foreach (var serviceName in root.GetSubKeyNames())
        {
            using var service = root.OpenSubKey(serviceName);
            if (service is null)
            {
                continue;
            }

            foreach (var instanceName in service.GetSubKeyNames())
            {
                var instanceId = $@"BTHLEDevice\{serviceName}\{instanceName}";
                if (!string.Equals(DeviceSelection.HidAddress(instanceId), selectedAddress,
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                isPresent = true;
                using var diagnostic = service.OpenSubKey(
                    $@"{instanceName}\Device Parameters\WUDFDiagnosticInfo");
                if (diagnostic?.GetValue("HostPid") is int hostPid && hostPid > 0)
                {
                    return (true, hostPid);
                }
            }
        }

        return (isPresent, null);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SpDevinfoData
    {
        public uint Size;
        public Guid ClassGuid;
        public uint DevInst;
        public UIntPtr Reserved;
    }

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr SetupDiGetClassDevs(
        IntPtr classGuid,
        string? enumerator,
        IntPtr parentWindow,
        uint flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiEnumDeviceInfo(
        IntPtr deviceInfoSet,
        uint memberIndex,
        ref SpDevinfoData deviceInfoData);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiGetDeviceInstanceId(
        IntPtr deviceInfoSet,
        ref SpDevinfoData deviceInfoData,
        StringBuilder deviceInstanceId,
        int deviceInstanceIdSize,
        out int requiredSize);

    [DllImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);
}
