using Microsoft.Win32;
using SayAll.HidBridge.Contracts;

namespace SayAll.HidBridge;

internal static class Rc003RegistryLocator
{
    private const string BluetoothLeDevicesKey =
        @"SYSTEM\CurrentControlSet\Enum\BTHLEDevice";
    private const string WudfDiagnosticKey =
        @"Device Parameters\WUDFDiagnosticInfo";

    public static Rc003HostCandidate? FindSingleSupportedHost()
    {
        var candidates = new List<Rc003HostCandidate>();
        using var root = Registry.LocalMachine.OpenSubKey(BluetoothLeDevicesKey);
        if (root is null)
        {
            return null;
        }

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
                using var diagnostic = service.OpenSubKey(
                    $@"{instanceName}\{WudfDiagnosticKey}");
                var hostPid = diagnostic?.GetValue("HostPid") switch
                {
                    int value => value,
                    long value when value is > 0 and <= int.MaxValue => (int)value,
                    _ => 0,
                };
                candidates.Add(new Rc003HostCandidate(instanceId, hostPid));
            }
        }

        var selection = DeviceSelection.Read();
        return selection is null ? null :
            Rc003TargetSelector.SelectForDevice(candidates, selection.BluetoothAddress, selection.HidHardwareToken);
    }
}
