namespace SayAll.HidBridge.Contracts;

public static class HidBridgeServiceContract
{
    public static string ProductName => "VoiceAnything";

    public static string ServiceName => "VoiceAnythingHidBridge";

    public static string LegacyServiceName => "SayAllHidBridge";

    public static string DisplayName => "VoiceAnything RC003MS Button Bridge";

    public static string SharedRootDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        ProductName,
        "HidBridge");

    public static string SharedEventLogPath { get; } = Path.Combine(
        SharedRootDirectory,
        "events.jsonl");
}
