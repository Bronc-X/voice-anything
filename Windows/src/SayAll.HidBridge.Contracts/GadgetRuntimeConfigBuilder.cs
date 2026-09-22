using System.Text.Json;

namespace SayAll.HidBridge.Contracts;

public static class GadgetRuntimeConfigBuilder
{
    public const string GadgetFileName = "RemoteMicRC003HidTap.dll";
    public const string ConfigFileName = "RemoteMicRC003HidTap.config";
    public const string ScriptFileName = "rc003_hid_gadget.js";

    public static string Build()
    {
        var config = new
        {
            interaction = new
            {
                type = "script",
                path = ScriptFileName,
                on_change = "ignore",
            },
            runtime = "qjs",
            teardown = "minimal",
        };

        return JsonSerializer.Serialize(
            config,
            new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine;
    }
}
