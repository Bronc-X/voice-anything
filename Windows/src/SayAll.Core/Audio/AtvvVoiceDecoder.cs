namespace SayAll.Core.Audio;

public sealed class AtvvVoiceDecoder
{
    private readonly int frameSize;
    private readonly FrameAccumulator accumulator = new();
    private readonly ImaAdpcmDecoder decoder = new();

    public AtvvVoiceDecoder(int frameSize)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(frameSize);
        this.frameSize = frameSize;
    }

    public bool IsStreaming { get; private set; }

    public void ApplyControl(AtvvControlPacket packet)
    {
        ArgumentNullException.ThrowIfNull(packet);

        switch (packet.Kind)
        {
            case AtvvControlKind.StreamStarted:
                IsStreaming = true;
                accumulator.Reset();
                break;
            case AtvvControlKind.StreamStopped:
                IsStreaming = false;
                accumulator.Reset();
                decoder.Reset();
                break;
            case AtvvControlKind.DecoderSynchronized:
                accumulator.Reset();
                decoder.Reset(packet.Predictor, packet.StepIndex);
                break;
        }
    }

    public IReadOnlyList<short[]> AppendAudio(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);

        if (!IsStreaming)
        {
            return [];
        }

        return accumulator
            .Append(data, frameSize)
            .Select(frame => PcmPostprocessor.Process(decoder.Decode(frame), 0d))
            .ToArray();
    }
}
