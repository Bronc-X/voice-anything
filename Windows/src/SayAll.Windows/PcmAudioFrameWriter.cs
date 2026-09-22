using System.Runtime.InteropServices;
using Windows.Media;

namespace SayAll.Windows;

public static class PcmAudioFrameWriter
{
    public static unsafe void Write(AudioFrame frame, short[] samples)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(samples);

        using var buffer = frame.LockBuffer(AudioBufferAccessMode.Write);
        using var reference = buffer.CreateReference();
        var byteAccess = WinRT.CastExtensions.As<IMemoryBufferByteAccess>(reference);
        byteAccess.GetBuffer(out var destination, out var capacity);
        var byteCount = samples.Length * sizeof(short);
        if (capacity < byteCount)
        {
            throw new InvalidOperationException("Windows 音频缓冲区容量不足");
        }

        fixed (short* source = samples)
        {
            Buffer.MemoryCopy(source, destination, capacity, byteCount);
        }
    }

    [ComImport]
    [Guid("5B0D3235-4DBA-4D44-865E-8F1D0E4FD04D")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private unsafe interface IMemoryBufferByteAccess
    {
        void GetBuffer(out byte* buffer, out uint capacity);
    }
}
