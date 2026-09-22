namespace SayAll.HidBridge.Contracts;

public sealed record Rc003HostCandidate(string InstanceId, int HostPid);

public static class Rc003TargetSelector
{
    public static Rc003HostCandidate? SelectForDevice(
        IReadOnlyList<Rc003HostCandidate> candidates, string address)
    {
        if (!DeviceSelection.ValidAddress(address)) return null;
        var matching = candidates.Where(candidate => candidate.HostPid > 0 &&
            string.Equals(DeviceSelection.HidAddress(candidate.InstanceId), address,
                StringComparison.OrdinalIgnoreCase)).ToArray();
        if (matching.Length != 1) return null;
        // The capture hook covers the host. A shared host can mix unrelated HID reports.
        if (candidates.Any(candidate => candidate.HostPid == matching[0].HostPid &&
            candidate.InstanceId != matching[0].InstanceId)) return null;
        return matching[0];
    }

    public static Rc003HostCandidate? SelectSingleSupportedHost(
        IReadOnlyList<Rc003HostCandidate> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);

        Rc003HostCandidate? selected = null;
        foreach (var candidate in candidates)
        {
            if (candidate.HostPid <= 0 ||
                !Rc003DriverContract.IsSupportedDeviceInstanceId(candidate.InstanceId))
            {
                continue;
            }

            if (selected is not null)
            {
                return null;
            }

            selected = candidate;
        }

        return selected;
    }
}
