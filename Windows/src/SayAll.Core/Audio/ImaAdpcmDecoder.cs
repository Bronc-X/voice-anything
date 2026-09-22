namespace SayAll.Core.Audio;

public sealed class ImaAdpcmDecoder
{
    private static readonly int[] StepTable =
    [
        7, 8, 9, 10, 11, 12, 13, 14, 16, 17, 19, 21, 23, 25, 28, 31,
        34, 37, 41, 45, 50, 55, 60, 66, 73, 80, 88, 97, 107, 118, 130,
        143, 157, 173, 190, 209, 230, 253, 279, 307, 337, 371, 408, 449,
        494, 544, 598, 658, 724, 796, 876, 963, 1060, 1166, 1282, 1411,
        1552, 1707, 1878, 2066, 2272, 2499, 2749, 3024, 3327, 3660, 4026,
        4428, 4871, 5358, 5894, 6484, 7132, 7845, 8630, 9493, 10442,
        11487, 12635, 13899, 15289, 16818, 18500, 20350, 22385, 24623,
        27086, 29794, 32767,
    ];

    private static readonly int[] IndexTable = [-1, -1, -1, -1, 2, 4, 6, 8];

    public int Predictor { get; private set; }

    public int StepIndex { get; private set; }

    public void Reset(int predictor = 0, int stepIndex = 0)
    {
        Predictor = Math.Clamp(predictor, short.MinValue, short.MaxValue);
        StepIndex = Math.Clamp(stepIndex, 0, StepTable.Length - 1);
    }

    public short[] Decode(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);

        var samples = new short[data.Length * 2];
        var sampleIndex = 0;
        foreach (var value in data)
        {
            samples[sampleIndex++] = DecodeNibble(value >> 4);
            samples[sampleIndex++] = DecodeNibble(value & 0x0F);
        }

        return samples;
    }

    private short DecodeNibble(int nibble)
    {
        var step = StepTable[StepIndex];
        var difference = step >> 3;
        if ((nibble & 1) != 0)
        {
            difference += step >> 2;
        }

        if ((nibble & 2) != 0)
        {
            difference += step >> 1;
        }

        if ((nibble & 4) != 0)
        {
            difference += step;
        }

        Predictor += (nibble & 8) != 0 ? -difference : difference;
        Predictor = Math.Clamp(Predictor, short.MinValue, short.MaxValue);
        StepIndex += IndexTable[nibble & 7];
        StepIndex = Math.Clamp(StepIndex, 0, StepTable.Length - 1);
        return (short)Predictor;
    }
}
