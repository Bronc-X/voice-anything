namespace SayAll.Core.Audio;

public sealed record AtvvCapabilities(
    ushort Version,
    byte Codecs,
    byte Interaction,
    int FrameSize,
    byte SelectedCodec,
    double SampleRate)
{
    public static AtvvCapabilities? Parse(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);

        if (data.Length < 7 || data[0] != 0x0B)
        {
            return null;
        }

        var version = (ushort)(data[1] << 8 | data[2]);
        byte codecs;
        byte interaction;

        if (version >= 0x0100)
        {
            codecs = data[3];
            interaction = data[4];
            if (codecs == 0 && data.Length >= 9 && (data[4] & 0x03) != 0)
            {
                codecs = data[4];
                interaction = 0x03;
            }
        }
        else
        {
            if (data.Length < 9)
            {
                return null;
            }

            codecs = data[4];
            interaction = 0;
        }

        var declaredFrameSize = data[5] << 8 | data[6];
        var selectedCodec = (byte)((codecs & 0x02) != 0 ? 0x02 : 0x01);

        return new AtvvCapabilities(
            version,
            codecs,
            interaction,
            declaredFrameSize == 0 ? 120 : declaredFrameSize,
            selectedCodec,
            selectedCodec == 0x02 ? 16_000d : 8_000d);
    }
}
