namespace SayAll.Core.Audio;

public sealed record VoiceSpectrumFrame(
    byte[] Intensities,
    double PeakDbFs,
    double DominantFrequencyHz);

public static class VoiceSpectrumAnalyzer
{
    public const int BandCount = 24;
    private const int SampleRate = 16_000;
    private const int AnalysisWindowSize = 256;
    private const double MinimumFrequency = 80;
    private const double MaximumFrequency = 7_600;
    private const double VisualFloorDb = -72;
    private const double VisualCeilingDb = -3;

    public static VoiceSpectrumFrame Analyze(short[] samples)
    {
        ArgumentNullException.ThrowIfNull(samples);
        if (samples.Length == 0)
        {
            return SilentFrame();
        }

        var sampleOffset = Math.Max(0, samples.Length - AnalysisWindowSize);
        var sampleCount = Math.Min(samples.Length, AnalysisWindowSize);
        var peak = 0;
        for (var index = sampleOffset; index < samples.Length; index++)
        {
            peak = Math.Max(peak, Math.Abs((int)samples[index]));
        }

        if (peak == 0)
        {
            return SilentFrame();
        }

        var intensities = new byte[BandCount];
        var dominantMagnitude = double.MinValue;
        var dominantFrequency = MinimumFrequency;
        var windowSum = 0d;
        for (var index = 0; index < sampleCount; index++)
        {
            windowSum += Hann(index, sampleCount);
        }

        for (var band = 0; band < BandCount; band++)
        {
            var frequency = BandFrequency(band);
            var real = 0d;
            var imaginary = 0d;
            for (var index = 0; index < sampleCount; index++)
            {
                var windowedSample = samples[sampleOffset + index] * Hann(index, sampleCount);
                var phase = 2 * Math.PI * frequency * index / SampleRate;
                real += windowedSample * Math.Cos(phase);
                imaginary -= windowedSample * Math.Sin(phase);
            }

            var magnitude = Math.Sqrt(real * real + imaginary * imaginary);
            if (magnitude > dominantMagnitude)
            {
                dominantMagnitude = magnitude;
                dominantFrequency = frequency;
            }

            var normalizedAmplitude = Math.Min(
                1,
                2 * magnitude / Math.Max(1, windowSum * short.MaxValue));
            var db = normalizedAmplitude <= 0
                ? -120
                : 20 * Math.Log10(normalizedAmplitude);
            var visual = Math.Clamp(
                (db - VisualFloorDb) / (VisualCeilingDb - VisualFloorDb),
                0,
                1);
            intensities[band] = (byte)Math.Round(Math.Pow(visual, 1.35) * byte.MaxValue);
        }

        return new VoiceSpectrumFrame(
            intensities,
            20 * Math.Log10(peak / (double)short.MaxValue),
            dominantFrequency);
    }

    private static VoiceSpectrumFrame SilentFrame()
    {
        return new VoiceSpectrumFrame(new byte[BandCount], -120, 0);
    }

    private static double BandFrequency(int band)
    {
        var progress = band / (double)(BandCount - 1);
        return MinimumFrequency * Math.Pow(MaximumFrequency / MinimumFrequency, progress);
    }

    private static double Hann(int index, int count)
    {
        return count <= 1
            ? 1
            : 0.5 - 0.5 * Math.Cos(2 * Math.PI * index / (count - 1));
    }
}
