using System.Runtime.InteropServices;
using Windows.Media.Devices;

namespace SayAll.Windows;

public enum AudioCaptureEndpointRole
{
    Console,
    Multimedia,
    Communications,
}

public sealed record AudioCaptureEndpointChange(
    AudioCaptureEndpointRole Role,
    string EndpointId);

public static class AudioCaptureEndpointId
{
    private const string WinRtMarker = "MMDEVAPI#";
    private const string PnpMarker = "MMDEVAPI\\";

    public static string Normalize(string deviceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceId);

        var winRtMarkerIndex = deviceId.IndexOf(
            WinRtMarker,
            StringComparison.OrdinalIgnoreCase);
        if (winRtMarkerIndex >= 0)
        {
            var endpointStart = winRtMarkerIndex + WinRtMarker.Length;
            var endpointEnd = deviceId.IndexOf('#', endpointStart);
            return endpointEnd > endpointStart
                ? deviceId[endpointStart..endpointEnd]
                : deviceId[endpointStart..];
        }

        var pnpMarkerIndex = deviceId.IndexOf(
            PnpMarker,
            StringComparison.OrdinalIgnoreCase);
        return pnpMarkerIndex >= 0
            ? deviceId[(pnpMarkerIndex + PnpMarker.Length)..]
            : deviceId;
    }
}

public sealed record AudioCaptureRoutePlan(
    IReadOnlyList<AudioCaptureEndpointChange> Activate,
    IReadOnlyList<AudioCaptureEndpointChange> Restore)
{
    public static AudioCaptureRoutePlan Create(
        string previousDefaultEndpointId,
        string previousCommunicationsEndpointId,
        string cableOutputEndpointId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(previousDefaultEndpointId);
        ArgumentException.ThrowIfNullOrWhiteSpace(previousCommunicationsEndpointId);
        ArgumentException.ThrowIfNullOrWhiteSpace(cableOutputEndpointId);

        return new AudioCaptureRoutePlan(
            [
                new(AudioCaptureEndpointRole.Console, cableOutputEndpointId),
                new(AudioCaptureEndpointRole.Multimedia, cableOutputEndpointId),
                new(AudioCaptureEndpointRole.Communications, cableOutputEndpointId),
            ],
            [
                new(AudioCaptureEndpointRole.Console, previousDefaultEndpointId),
                new(AudioCaptureEndpointRole.Multimedia, previousDefaultEndpointId),
                new(AudioCaptureEndpointRole.Communications, previousCommunicationsEndpointId),
            ]);
    }
}

public sealed class DefaultAudioCaptureRoute : IDisposable
{
    private static readonly Guid PolicyConfigClassId =
        Guid.Parse("870AF99C-171D-4F9E-AF0D-E63DF40C2BC9");

    private readonly IPolicyConfig policyConfig;
    private readonly AudioCaptureRoutePlan plan;
    private bool disposed;

    private DefaultAudioCaptureRoute(
        IPolicyConfig policyConfig,
        AudioCaptureRoutePlan plan)
    {
        this.policyConfig = policyConfig;
        this.plan = plan;
    }

    public static DefaultAudioCaptureRoute Open(string cableOutputEndpointId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cableOutputEndpointId);
        var previousDefault = AudioCaptureEndpointId.Normalize(
            MediaDevice.GetDefaultAudioCaptureId(AudioDeviceRole.Default));
        var previousCommunications = AudioCaptureEndpointId.Normalize(
            MediaDevice.GetDefaultAudioCaptureId(AudioDeviceRole.Communications));
        var cableOutput = AudioCaptureEndpointId.Normalize(cableOutputEndpointId);
        var plan = AudioCaptureRoutePlan.Create(
            previousDefault,
            previousCommunications,
            cableOutput);
        var type = Type.GetTypeFromCLSID(PolicyConfigClassId, throwOnError: true)
            ?? throw new InvalidOperationException("Windows 默认录音设备服务不可用。" );
        var instance = Activator.CreateInstance(type)
            ?? throw new InvalidOperationException("无法创建 Windows 录音设备路由服务。" );
        var route = new DefaultAudioCaptureRoute((IPolicyConfig)instance, plan);
        try
        {
            route.Apply(plan.Activate);
            return route;
        }
        catch
        {
            route.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        try
        {
            Apply(plan.Restore);
        }
        finally
        {
            if (Marshal.IsComObject(policyConfig))
            {
                Marshal.FinalReleaseComObject(policyConfig);
            }
        }
    }

    private void Apply(IEnumerable<AudioCaptureEndpointChange> changes)
    {
        foreach (var change in changes)
        {
            Marshal.ThrowExceptionForHR(policyConfig.SetDefaultEndpoint(
                change.EndpointId,
                change.Role));
        }
    }

    [ComImport]
    [Guid("F8679F50-850A-41CF-9C72-430F290290C8")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPolicyConfig
    {
        [PreserveSig]
        int GetMixFormat([MarshalAs(UnmanagedType.LPWStr)] string deviceId, out IntPtr format);

        [PreserveSig]
        int GetDeviceFormat(
            [MarshalAs(UnmanagedType.LPWStr)] string deviceId,
            int defaultFormat,
            out IntPtr format);

        [PreserveSig]
        int ResetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string deviceId);

        [PreserveSig]
        int SetDeviceFormat(
            [MarshalAs(UnmanagedType.LPWStr)] string deviceId,
            IntPtr endpointFormat,
            IntPtr mixFormat);

        [PreserveSig]
        int GetProcessingPeriod(
            [MarshalAs(UnmanagedType.LPWStr)] string deviceId,
            int defaultPeriod,
            out long period,
            out long minimumPeriod);

        [PreserveSig]
        int SetProcessingPeriod(
            [MarshalAs(UnmanagedType.LPWStr)] string deviceId,
            IntPtr period);

        [PreserveSig]
        int GetShareMode([MarshalAs(UnmanagedType.LPWStr)] string deviceId, out IntPtr mode);

        [PreserveSig]
        int SetShareMode([MarshalAs(UnmanagedType.LPWStr)] string deviceId, IntPtr mode);

        [PreserveSig]
        int GetPropertyValue(
            [MarshalAs(UnmanagedType.LPWStr)] string deviceId,
            IntPtr key,
            out IntPtr value);

        [PreserveSig]
        int SetPropertyValue(
            [MarshalAs(UnmanagedType.LPWStr)] string deviceId,
            IntPtr key,
            IntPtr value);

        [PreserveSig]
        int SetDefaultEndpoint(
            [MarshalAs(UnmanagedType.LPWStr)] string deviceId,
            AudioCaptureEndpointRole role);

        [PreserveSig]
        int SetEndpointVisibility(
            [MarshalAs(UnmanagedType.LPWStr)] string deviceId,
            int visible);
    }
}
