using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SayAll.Windows;

public static class CodexKeybindingProvisioner
{
    public const string ReasoningCommandId = "composer.cycleReasoningEffort";
    public const string ReasoningAccelerator = "Ctrl+Alt+Shift+F12";
    public const string ActivityCommandId = "togglePriorityFilter";
    public const string ActivityAccelerator = "Ctrl+Alt+Right";
    private const string ObsoleteDictationCommandId = "globalDictationToggle";
    private const string ObsoleteDictationAccelerator = "Ctrl+Alt+Shift+F11";

    public static string CurrentUserPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".codex",
        "keybindings.json");

    public static bool EnsureReasoningShortcut(string? path = null)
    {
        path ??= CurrentUserPath;
        JsonArray bindings;
        if (File.Exists(path))
        {
            try
            {
                bindings = JsonNode.Parse(File.ReadAllText(path)) as JsonArray
                    ?? throw new InvalidDataException(
                        "Codex keybindings.json 必须是一个 JSON 数组。");
            }
            catch (JsonException exception)
            {
                throw new InvalidDataException(
                    "Codex keybindings.json 内容无效，未修改原文件。",
                    exception);
            }
        }
        else
        {
            bindings = [];
        }

        var removedObsoleteDictationShortcut = false;
        for (var index = bindings.Count - 1; index >= 0; index--)
        {
            if (bindings[index] is not JsonObject binding)
            {
                continue;
            }

            if (string.Equals(
                    binding["command"]?.GetValue<string>(),
                    ObsoleteDictationCommandId,
                    StringComparison.Ordinal) &&
                string.Equals(
                    binding["key"]?.GetValue<string>(),
                    ObsoleteDictationAccelerator,
                    StringComparison.OrdinalIgnoreCase))
            {
                bindings.RemoveAt(index);
                removedObsoleteDictationShortcut = true;
            }
        }

        var hasReasoningShortcut = false;
        var hasActivityShortcut = false;
        foreach (var node in bindings.OfType<JsonObject>())
        {
            var command = node["command"]?.GetValue<string>();
            var key = node["key"]?.GetValue<string>();
            if (string.Equals(command, ReasoningCommandId, StringComparison.Ordinal) &&
                string.Equals(key, ReasoningAccelerator, StringComparison.OrdinalIgnoreCase))
            {
                hasReasoningShortcut = true;
            }

            if (string.Equals(command, ActivityCommandId, StringComparison.Ordinal) &&
                string.Equals(key, ActivityAccelerator, StringComparison.OrdinalIgnoreCase))
            {
                hasActivityShortcut = true;
            }

            if (!string.Equals(command, ReasoningCommandId, StringComparison.Ordinal) &&
                string.Equals(key, ReasoningAccelerator, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Codex 快捷键 {ReasoningAccelerator} 已被 {command} 使用，未覆盖你的配置。");
            }

            if (!string.Equals(command, ActivityCommandId, StringComparison.Ordinal) &&
                string.Equals(key, ActivityAccelerator, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Codex 快捷键 {ActivityAccelerator} 已被 {command} 使用，未覆盖你的配置。");
            }
        }

        if (hasReasoningShortcut &&
            hasActivityShortcut &&
            !removedObsoleteDictationShortcut)
        {
            return false;
        }

        if (!hasReasoningShortcut)
        {
            bindings.Add(new JsonObject
            {
                ["command"] = ReasoningCommandId,
                ["key"] = ReasoningAccelerator,
            });
        }

        if (!hasActivityShortcut)
        {
            bindings.Add(new JsonObject
            {
                ["command"] = ActivityCommandId,
                ["key"] = ActivityAccelerator,
            });
        }

        var directory = Path.GetDirectoryName(path)
            ?? throw new InvalidOperationException("无法确定 Codex 快捷键目录。");
        Directory.CreateDirectory(directory);
        var temporaryPath = path + ".vibecontrol.tmp";
        File.WriteAllText(
            temporaryPath,
            bindings.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporaryPath, path, overwrite: true);
        return true;
    }
}
