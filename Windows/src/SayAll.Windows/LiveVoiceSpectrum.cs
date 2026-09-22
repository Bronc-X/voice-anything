using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SayAll.Core.Audio;

namespace SayAll.Windows;

public enum SpectrumPalette
{
    Blue,
    Ember,
    Emerald,
}

public readonly record struct SpectrumColor(byte Red, byte Green, byte Blue);

public static class SpectrumPaletteColorizer
{
    public static SpectrumColor Colorize(SpectrumPalette palette, double value)
    {
        value = Math.Clamp(value, 0, 1);
        return palette switch
        {
            SpectrumPalette.Ember => Ember(value),
            SpectrumPalette.Emerald => Emerald(value),
            _ => Blue(value),
        };
    }

    private static SpectrumColor Blue(double value) => value switch
    {
        < 0.08 => Lerp(new(4, 5, 16), new(6, 12, 51), value / 0.08),
        < 0.32 => Lerp(new(6, 12, 51), new(5, 44, 142), (value - 0.08) / 0.24),
        < 0.62 => Lerp(new(5, 44, 142), new(0, 119, 255), (value - 0.32) / 0.30),
        < 0.84 => Lerp(new(0, 119, 255), new(70, 220, 255), (value - 0.62) / 0.22),
        _ => Lerp(new(70, 220, 255), new(222, 255, 255), (value - 0.84) / 0.16),
    };

    private static SpectrumColor Ember(double value) => value switch
    {
        < 0.08 => Lerp(new(5, 3, 4), new(22, 3, 4), value / 0.08),
        < 0.32 => Lerp(new(22, 3, 4), new(113, 13, 5), (value - 0.08) / 0.24),
        < 0.62 => Lerp(new(113, 13, 5), new(232, 55, 5), (value - 0.32) / 0.30),
        < 0.84 => Lerp(new(232, 55, 5), new(255, 151, 30), (value - 0.62) / 0.22),
        _ => Lerp(new(255, 151, 30), new(255, 225, 129), (value - 0.84) / 0.16),
    };

    private static SpectrumColor Emerald(double value) => value switch
    {
        < 0.08 => Lerp(new(2, 7, 5), new(2, 24, 13), value / 0.08),
        < 0.32 => Lerp(new(2, 24, 13), new(3, 86, 36), (value - 0.08) / 0.24),
        < 0.62 => Lerp(new(3, 86, 36), new(8, 184, 82), (value - 0.32) / 0.30),
        < 0.84 => Lerp(new(8, 184, 82), new(91, 255, 128), (value - 0.62) / 0.22),
        _ => Lerp(new(91, 255, 128), new(222, 255, 218), (value - 0.84) / 0.16),
    };

    private static SpectrumColor Lerp(
        SpectrumColor from,
        SpectrumColor to,
        double progress)
    {
        progress = Math.Clamp(progress, 0, 1);
        return new SpectrumColor(
            (byte)Math.Round(from.Red + (to.Red - from.Red) * progress),
            (byte)Math.Round(from.Green + (to.Green - from.Green) * progress),
            (byte)Math.Round(from.Blue + (to.Blue - from.Blue) * progress));
    }
}

public sealed class LiveVoiceSpectrum : Image
{
    private const int PixelWidth = 384;
    private const int PixelHeight = 156;
    private const int BytesPerPixel = 4;
    private const int PixelsPerSlice = 2;
    private readonly WriteableBitmap bitmap;
    private readonly byte[] pixels = new byte[PixelWidth * PixelHeight * BytesPerPixel];
    private readonly byte[] energyPixels = new byte[PixelWidth * PixelHeight];
    private readonly double[] smoothedBands = new double[VoiceSpectrumAnalyzer.BandCount];
    private bool recording;

    public SpectrumPalette Palette { get; private set; } = SpectrumPalette.Blue;

    public LiveVoiceSpectrum()
    {
        bitmap = new WriteableBitmap(
            PixelWidth,
            PixelHeight,
            96,
            96,
            PixelFormats.Bgra32,
            null);
        Source = bitmap;
        Stretch = Stretch.Fill;
        SnapsToDevicePixels = true;
        Reset();
    }

    public void Reset()
    {
        Array.Clear(smoothedBands);
        Array.Clear(energyPixels);
        RenderAllPixels();
        Commit();
    }

    public void SetPalette(SpectrumPalette palette)
    {
        if (!Enum.IsDefined(palette))
        {
            palette = SpectrumPalette.Blue;
        }

        if (Palette == palette)
        {
            return;
        }

        Palette = palette;
        RenderAllPixels();
        Commit();
    }

    public void SetRecording(bool value)
    {
        if (value && !recording)
        {
            Reset();
        }

        recording = value;
    }

    public void PushFrame(VoiceSpectrumFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        PushFrames([frame]);
    }

    public void PushFrames(IEnumerable<VoiceSpectrumFrame> frames)
    {
        ArgumentNullException.ThrowIfNull(frames);
        if (!recording)
        {
            return;
        }

        var changed = false;
        foreach (var frame in frames)
        {
            if (frame.Intensities.Length == 0)
            {
                continue;
            }

            PushFramePixels(frame);
            changed = true;
        }

        if (changed)
        {
            Commit();
        }
    }

    private void PushFramePixels(VoiceSpectrumFrame frame)
    {
        ScrollLeft();
        for (var band = 0; band < smoothedBands.Length; band++)
        {
            var target = frame.Intensities[Math.Min(band, frame.Intensities.Length - 1)];
            var response = target > smoothedBands[band] ? 0.82 : 0.34;
            smoothedBands[band] += (target - smoothedBands[band]) * response;
        }

        for (var y = 0; y < PixelHeight; y++)
        {
            var frequencyPosition = 1 - y / (double)(PixelHeight - 1);
            var bandPosition = frequencyPosition * (smoothedBands.Length - 1);
            var lowerBand = Math.Clamp((int)Math.Floor(bandPosition), 0, smoothedBands.Length - 1);
            var upperBand = Math.Min(smoothedBands.Length - 1, lowerBand + 1);
            var blend = bandPosition - lowerBand;
            var intensity = smoothedBands[lowerBand] * (1 - blend) +
                smoothedBands[upperBand] * blend;
            var lowFrequencyGlow = Math.Pow(1 - frequencyPosition, 1.8) * 12;
            intensity = Math.Clamp(intensity + lowFrequencyGlow, 0, byte.MaxValue);
            for (var x = PixelWidth - PixelsPerSlice; x < PixelWidth; x++)
            {
                var variation = x == PixelWidth - 1 ? 1d : 0.82;
                energyPixels[y * PixelWidth + x] = (byte)(intensity * variation);
                RenderPixel(x, y);
            }
        }
    }

    private void ScrollLeft()
    {
        var stride = PixelWidth * BytesPerPixel;
        var shift = PixelsPerSlice * BytesPerPixel;
        for (var y = 0; y < PixelHeight; y++)
        {
            var rowOffset = y * stride;
            Buffer.BlockCopy(
                pixels,
                rowOffset + shift,
                pixels,
                rowOffset,
                stride - shift);

            var energyRowOffset = y * PixelWidth;
            Buffer.BlockCopy(
                energyPixels,
                energyRowOffset + PixelsPerSlice,
                energyPixels,
                energyRowOffset,
                PixelWidth - PixelsPerSlice);
        }
    }

    private void RenderAllPixels()
    {
        for (var y = 0; y < PixelHeight; y++)
        {
            for (var x = 0; x < PixelWidth; x++)
            {
                RenderPixel(x, y);
            }
        }
    }

    private void RenderPixel(int x, int y)
    {
        var intensity = energyPixels[y * PixelWidth + x] / (double)byte.MaxValue;
        var color = SpectrumPaletteColorizer.Colorize(Palette, intensity);
        var grain = intensity == 0 && ((x * 17 + y * 31) % 23) == 0 ? 2 : 0;
        SetPixel(
            x,
            y,
            (byte)Math.Min(byte.MaxValue, color.Blue + grain),
            (byte)Math.Min(byte.MaxValue, color.Green + grain),
            (byte)Math.Min(byte.MaxValue, color.Red + grain));
    }

    private void Commit()
    {
        bitmap.WritePixels(
            new System.Windows.Int32Rect(0, 0, PixelWidth, PixelHeight),
            pixels,
            PixelWidth * BytesPerPixel,
            0);
    }

    private void SetPixel(int x, int y, byte blue, byte green, byte red)
    {
        var offset = (y * PixelWidth + x) * BytesPerPixel;
        pixels[offset] = blue;
        pixels[offset + 1] = green;
        pixels[offset + 2] = red;
        pixels[offset + 3] = byte.MaxValue;
    }

}
