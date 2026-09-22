namespace SayAll.Windows;

public static class RemoteActionTargetCatalog
{
    private static readonly HashSet<string> SupportedProcesses = new(
        StringComparer.OrdinalIgnoreCase)
    {
        "ChatGPT",
        "Codex",
        "Claude",
        "WindowsTerminal",
        "OpenConsole",
        "wezterm-gui",
        "alacritty",
    };

    public static bool SupportsProcess(string processName)
    {
        return !string.IsNullOrWhiteSpace(processName) &&
            SupportedProcesses.Contains(processName);
    }

    public static bool IsCodexProcess(string processName)
    {
        return processName.Equals("ChatGPT", StringComparison.OrdinalIgnoreCase) ||
            processName.Equals("Codex", StringComparison.OrdinalIgnoreCase);
    }
}
