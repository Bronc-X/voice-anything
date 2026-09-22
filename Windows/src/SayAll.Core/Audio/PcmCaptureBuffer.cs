namespace SayAll.Core.Audio;

public sealed class PcmCaptureBuffer
{
    private readonly Lock sync = new();
    private readonly List<short> samples = [];
    private double sumOfSquares;
    private int peakMagnitude;

    public long SampleCount
    {
        get
        {
            lock (sync)
            {
                return samples.Count;
            }
        }
    }

    public double PeakDbFs
    {
        get
        {
            lock (sync)
            {
                return ToDbFs(peakMagnitude);
            }
        }
    }

    public double RmsDbFs
    {
        get
        {
            lock (sync)
            {
                if (samples.Count == 0 || sumOfSquares <= 0d)
                {
                    return double.NegativeInfinity;
                }

                var rms = Math.Sqrt(sumOfSquares / samples.Count);
                return ToDbFs(rms);
            }
        }
    }

    public bool HasAudibleSignal => PeakDbFs >= -45d && RmsDbFs >= -55d;

    public void Append(short[] frame)
    {
        ArgumentNullException.ThrowIfNull(frame);

        lock (sync)
        {
            foreach (var sample in frame)
            {
                samples.Add(sample);
                var magnitude = Math.Abs((int)sample);
                peakMagnitude = Math.Max(peakMagnitude, magnitude);
                sumOfSquares += (double)sample * sample;
            }
        }
    }

    public void Reset()
    {
        lock (sync)
        {
            samples.Clear();
            sumOfSquares = 0d;
            peakMagnitude = 0;
        }
    }

    public short[] Snapshot()
    {
        lock (sync)
        {
            return [.. samples];
        }
    }

    public double DurationSeconds(double sampleRate)
    {
        if (!double.IsFinite(sampleRate) || sampleRate <= 0d)
        {
            return 0d;
        }

        return SampleCount / sampleRate;
    }

    private static double ToDbFs(double magnitude)
    {
        return magnitude <= 0d
            ? double.NegativeInfinity
            : 20d * Math.Log10(magnitude / short.MaxValue);
    }
}
