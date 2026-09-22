namespace SayAll.Core.Audio;

public static class AtvvProtocol
{
    public static readonly Guid ServiceUuid =
        Guid.Parse("AB5E0001-5A21-4F05-BC7D-AF01F617B664");
    public static readonly Guid TransmitUuid =
        Guid.Parse("AB5E0002-5A21-4F05-BC7D-AF01F617B664");
    public static readonly Guid AudioUuid =
        Guid.Parse("AB5E0003-5A21-4F05-BC7D-AF01F617B664");
    public static readonly Guid ControlUuid =
        Guid.Parse("AB5E0004-5A21-4F05-BC7D-AF01F617B664");

    public static byte[] GetCapabilitiesV10()
    {
        return [0x0A, 0x01, 0x00, 0x00, 0x03, 0x03];
    }

    public static bool SupportsAudio(double sampleRate)
    {
        return sampleRate == 16_000d;
    }

    public static byte[] MicrophoneOpen(ushort version, byte codec)
    {
        return version >= 0x0100 ? [0x0C, 0x00] : [0x0C, 0x00, codec];
    }

    public static byte[] MicrophoneClose(ushort version, byte sessionId)
    {
        return version >= 0x0100 ? [0x0D, sessionId] : [0x0D];
    }

    public static byte[]? MicrophoneExtend(ushort version, byte sessionId)
    {
        return version >= 0x0100 ? [0x0E, sessionId] : null;
    }
}
