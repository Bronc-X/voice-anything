namespace SayAll.Core.Audio;

public enum AtvvControlKind
{
    RemoteMicrophoneRequested,
    StreamStarted,
    StreamStopped,
    DecoderSynchronized,
}

public sealed record AtvvControlPacket(
    AtvvControlKind Kind,
    byte Codec = 0,
    byte SessionId = 0,
    int Predictor = 0,
    int StepIndex = 0)
{
    public static AtvvControlPacket? Parse(byte[] data)
    {
        if (data.Length == 0)
        {
            return null;
        }

        return data[0] switch
        {
            0x08 => new AtvvControlPacket(AtvvControlKind.RemoteMicrophoneRequested),
            0x04 when data.Length >= 4 => new AtvvControlPacket(
                AtvvControlKind.StreamStarted,
                Codec: data[2],
                SessionId: data[3]),
            0x00 => new AtvvControlPacket(AtvvControlKind.StreamStopped),
            0x0A when data.Length >= 7 => new AtvvControlPacket(
                AtvvControlKind.DecoderSynchronized,
                Predictor: (short)((data[4] << 8) | data[5]),
                StepIndex: data[6]),
            _ => null,
        };
    }
}
