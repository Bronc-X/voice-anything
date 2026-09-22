using System.IO;
using System.Windows.Media;

namespace SayAll.Windows;

public sealed class LocalVoicePlayer : IDisposable
{
    private readonly MediaPlayer _player = new();

    public bool UsesExternalProcess => false;

    public Uri? Source { get; private set; }

    public Uri Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("找不到本地试听文件。", fullPath);
        }

        if (!string.Equals(Path.GetExtension(fullPath), ".wav", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("试听文件必须是本地 WAV 录音。" );
        }

        using (var stream = new FileStream(
                   fullPath,
                   FileMode.Open,
                   FileAccess.Read,
                   FileShare.Read))
        {
            Span<byte> header = stackalloc byte[12];
            if (stream.Read(header) != header.Length ||
                !header[..4].SequenceEqual("RIFF"u8) ||
                !header[8..].SequenceEqual("WAVE"u8))
            {
                throw new InvalidDataException("试听文件不是有效的 WAV 录音。" );
            }
        }

        Source = new Uri(fullPath, UriKind.Absolute);
        _player.Open(Source);
        return Source;
    }

    public void Play()
    {
        if (Source is null)
        {
            throw new InvalidOperationException("请先载入本地试听文件。" );
        }

        _player.Position = TimeSpan.Zero;
        _player.Play();
    }

    public void Stop()
    {
        _player.Stop();
    }

    public void Dispose()
    {
        _player.Close();
    }
}
