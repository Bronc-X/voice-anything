namespace SayAll.Core.Audio;

public sealed class FrameAccumulator
{
    private readonly List<byte> pending = [];

    public byte[] Pending => [.. pending];

    public IReadOnlyList<byte[]> Append(byte[] data, int frameSize)
    {
        ArgumentNullException.ThrowIfNull(data);

        if (frameSize <= 0)
        {
            return [];
        }

        pending.AddRange(data);
        var frames = new List<byte[]>();
        while (pending.Count >= frameSize)
        {
            frames.Add(pending.GetRange(0, frameSize).ToArray());
            pending.RemoveRange(0, frameSize);
        }

        return frames;
    }

    public void Reset()
    {
        pending.Clear();
    }
}
