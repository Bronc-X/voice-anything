using System.Text;

namespace SayAll.Core.Audio;

public static class PcmWaveFile
{
    public static byte[] BuildMono16(short[] samples, int sampleRate)
    {
        ArgumentNullException.ThrowIfNull(samples);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);

        var dataLength = checked(samples.Length * sizeof(short));
        using var stream = new MemoryStream(capacity: checked(44 + dataLength));
        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);
        writer.Write(Encoding.ASCII.GetBytes("RIFF"));
        writer.Write(checked(36 + dataLength));
        writer.Write(Encoding.ASCII.GetBytes("WAVE"));
        writer.Write(Encoding.ASCII.GetBytes("fmt "));
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)1);
        writer.Write(sampleRate);
        writer.Write(checked(sampleRate * sizeof(short)));
        writer.Write((short)sizeof(short));
        writer.Write((short)16);
        writer.Write(Encoding.ASCII.GetBytes("data"));
        writer.Write(dataLength);
        foreach (var sample in samples)
        {
            writer.Write(sample);
        }

        writer.Flush();
        return stream.ToArray();
    }
}
