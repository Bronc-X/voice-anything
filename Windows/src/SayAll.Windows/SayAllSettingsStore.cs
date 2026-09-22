using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using SayAll.Core.Input;

namespace SayAll.Windows;

public sealed class SayAllSettings
{
    public Dictionary<string, SavedModelBindings> ModelBindings { get; set; } = [];
    public int ConfigurationVersion { get; set; }

    public string? PreferredInputMethodId { get; set; } =
        WindowsVoiceInputCatalog.TypelessId;

    public string ActivePreset { get; set; } = "Codex";

    public bool RemoteActionsEnabled { get; set; }

    public SpectrumPalette SpectrumPalette { get; set; } =
        global::SayAll.Windows.SpectrumPalette.Blue;

    public List<RemoteButtonBindingEntry> RemoteBindings { get; set; } =
        RemoteBindingPresets.CreateCodex().Bindings.ToList();
}

public sealed record SavedModelBindings(string Preset, List<RemoteButtonBindingEntry> Bindings);

public sealed class SayAllSettingsStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _path;
    private readonly string? _legacyPath;
    private readonly SemaphoreSlim saveGate = new(1, 1);

    public SayAllSettingsStore(string? path = null, string? legacyPath = null)
    {
        var useDefaultPath = path is null;
        _path = path ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "VoiceAnything",
            "settings.json");
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var vibeControlPath = Path.Combine(local, "VibeControl", "settings.json");
        _legacyPath = legacyPath ?? (useDefaultPath ? File.Exists(vibeControlPath) ? vibeControlPath :
            Path.Combine(local, "SayAll", "settings.json") : null);
    }

    public async Task<SayAllSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_path))
        {
            if (_legacyPath is null || !File.Exists(_legacyPath))
            {
                var settings = new SayAllSettings();
                SayAllSettingsMigration.Apply(settings);
                return settings;
            }

            var legacySettings = await ReadAsync(_legacyPath, cancellationToken);
            SayAllSettingsMigration.Apply(legacySettings);
            await SaveAsync(legacySettings, cancellationToken);
            return legacySettings;
        }

        var storedSettings = await ReadAsync(_path, cancellationToken);
        if (SayAllSettingsMigration.Apply(storedSettings))
        {
            await SaveAsync(storedSettings, cancellationToken);
        }

        return storedSettings;
    }

    private static async Task<SayAllSettings> ReadAsync(
        string path,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read);
            return await JsonSerializer.DeserializeAsync<SayAllSettings>(
                       stream,
                       SerializerOptions,
                       cancellationToken)
                   ?? throw new InvalidDataException("VoiceAnything 设置文件内容为空。" );
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("VoiceAnything 设置文件已损坏。", exception);
        }
    }

    public async Task SaveAsync(
        SayAllSettings settings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var directory = Path.GetDirectoryName(_path)
            ?? throw new InvalidOperationException("无法确定 VoiceAnything 设置目录。" );
        Directory.CreateDirectory(directory);

        // Snapshot before waiting so a second UI action cannot mutate a write in progress.
        var serialized = JsonSerializer.SerializeToUtf8Bytes(settings, SerializerOptions);
        await saveGate.WaitAsync(cancellationToken);
        var temporaryPath = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
        await using (var stream = new FileStream(
                         temporaryPath,
                         FileMode.Create,
                         FileAccess.Write,
                         FileShare.None))
        {
            await stream.WriteAsync(serialized, cancellationToken);
            await stream.FlushAsync(cancellationToken);
        }

        File.Move(temporaryPath, _path, overwrite: true);
        }
        finally { if (File.Exists(temporaryPath)) File.Delete(temporaryPath); saveGate.Release(); }
    }
}
