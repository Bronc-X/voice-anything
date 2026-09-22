using System.Text.Json;
using SayAll.Core.History;
using SayAll.HidBridge.Contracts;
using SayAll.Core.Devices;
using SayAll.Core.Input;

var root = Path.Combine(Path.GetTempPath(), "VoiceAnything.Tests", Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
try
{
    var profile = new DeviceProfile(1, "example-device", "Example controller", "xiaomi-atvv-v1", ["EXAMPLE-1"],
        null, 0.5, new(false, false, false, false, false),
        [new("CaptureNote", "Capture note", 0x123, .1, .1, .2, .2, ["single", "long"])],
        new() { ["windows"] = "candidate", ["macos"] = "candidate" });
    profile.Validate();
    var incompleteProfile = JsonSerializer.Serialize(profile, DeviceProfile.JsonOptions).Replace("\"holdToTalk\":false,", "");
    Throws(() => JsonSerializer.Deserialize<DeviceProfile>(incompleteProfile, DeviceProfile.JsonOptions), "missing hardware capability is rejected instead of defaulted");
    var button = new RemoteButton("CaptureNote");
    var bindings = new RemoteBindingProfile("Example", [new(button, RemoteButtonTrigger.LongPress, new(RemoteButtonAction.NewChat))]);
    Require(bindings.Resolve(button, RemoteButtonTrigger.LongPress).Action == RemoteButtonAction.NewChat &&
        JsonSerializer.Deserialize<RemoteButton>(JsonSerializer.Serialize(button)) == button,
        "custom hardware controls preserve ID through binding and persistence");
    Require(profile.MatchesModel("example-1") && !profile.MatchesModel("NOT-EXAMPLE-1"), "hardware models match exactly, never by substring");
    var transport = new DeviceTransport(0x1234, 0x5678, 1, 2, ["Example remote"]);
    (profile with { Transport = transport }).Validate();
    Require(DeviceSelection.ValidHardwareToken(transport.HardwareToken), "hardware metadata produces a bounded exact HID identifier");
    Throws(() => (profile with { Transport = transport with { VendorIdSource = 9 } }).Validate(), "unsupported HID identifier source rejected");
    Throws(() => (profile with { Controls = [profile.Controls[0], profile.Controls[0] with { Id = "Other" }] }).Validate(), "duplicate HID usages rejected");
    Throws(() => (profile with { Capabilities = new(false, false, false, true, false) }).Validate(), "unsupported capabilities cannot be advertised");
    Throws(() => (profile with { Artwork = "../outside.png" }).ResolveArtwork(root), "artwork cannot escape profile directory");
    Throws(() => (profile with { Controls = [profile.Controls[0] with { X = double.NaN }] }).Validate(), "invalid layout coordinates rejected");
    var customGesture = new RemoteButtonGestureRecognizer(doubleClickButtons: []);
    customGesture.Process([button], DateTimeOffset.UnixEpoch);
    Require(customGesture.Flush(DateTimeOffset.UnixEpoch.AddSeconds(1)).Single().Button == button, "custom controls participate in real gesture recognition");
    var store = new JournalStore(root);
    Require(!store.Read().RecordReflections && !store.Read().AgentAccessEnabled, "private history starts disabled");
    var sharedDirectory = Path.Combine(root, "shared");
    Directory.CreateDirectory(sharedDirectory);
    File.Copy(Path.Combine(AppContext.BaseDirectory, "journal-v1.json"), Path.Combine(sharedDirectory, "journal.json"));
    var shared = new JournalStore(sharedDirectory);
    Require(shared.Summarize().ButtonPresses == 42 && shared.Summarize().VoiceSeconds == 16 &&
        shared.Search("CAFÉ").Single().Text == "共享记录 café：只保存本次新增的文字。",
        "shared Windows/macOS fixture preserves dates, statistics and Unicode");
    var now = DateTimeOffset.Parse("2026-09-21T23:59:50+00:00");
    Parallel.For(0, 100, _ => store.RecordButton(now, TimeZoneInfo.Utc));
    store.RecordVoice(now, now.AddSeconds(20), 16, TimeZoneInfo.Utc);
    var today = store.Summarize("2026-09-21", "2026-09-21");
    var tomorrow = store.Summarize("2026-09-22", "2026-09-22");
    Require(today.ButtonPresses == 100 && today.VoiceSeconds == 8 && today.VoiceSessions == 1 &&
        tomorrow.VoiceSeconds == 8 && tomorrow.VoiceSessions == 0, "day boundary splits duration, counts session once, serializes concurrent updates");
    Require(new JournalStore(root).Summarize().VoiceSeconds == 16, "stats survive restart");
    Throws(() => store.RecordVoice(now, now.AddSeconds(1), double.NaN, TimeZoneInfo.Utc), "invalid audio duration rejected");
    Throws(() => store.Summarize("2026-09-22", "2026-09-21"), "reversed date range rejected");
    var record = new Reflection(Guid.NewGuid().ToString(), Guid.NewGuid().ToString(), now, now.AddSeconds(20),
        "Editor", "xiaomi-rc003", "真实转写示例 café", "accessibility-delta-v1");
    Require(!store.Append(record), "disabled history never saves transcript");
    store.SetPrivacy(true, true);
    Require(store.Append(record) && !store.Append(record with { Id = Guid.NewGuid().ToString() }), "one record per voice session");
    Require(store.Search("CAFÉ", "editor").Count == 1 && store.Search("不存在").Count == 0,
        "unicode search, application filter and empty results");
    Require(TranscriptDelta.Extract("前文后文", "前文新内容后文", 2, 0) == "新内容", "only inserted text captured");
    Require(TranscriptDelta.Extract("秘密旧文尾部", "秘密新内容尾部", 2, 2) == "新内容", "selection replacement excludes context");
    Require(TranscriptDelta.Extract("已有文本", "已有文本", 4, 0) is null &&
        TranscriptDelta.Extract("前文后文", "整个页面被替换", 2, 0) is null,
        "unchanged and unrelated edits never archived");
    var grant = store.GrantAgent("Test client");
    Require(store.IsAuthorized(grant.Token) && !store.IsAuthorized(new string('0', 64)), "per-client authorization");
    Require(!File.ReadAllText(Path.Combine(root, "journal.json")).Contains(grant.Token), "plaintext token never persisted");
    var mcp = new HistoryMcpServer(store, grant.Token);
    Require(HasError(mcp.Handle("{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"tools/list\"}"), -32002), "MCP requires handshake");
    Require(!HasError(mcp.Handle("{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"initialize\",\"params\":{\"protocolVersion\":\"2025-06-18\"}}")), "MCP initializes");
    Require(mcp.Handle("{\"jsonrpc\":\"2.0\",\"method\":\"notifications/initialized\"}") is null, "notifications have no response");
    var searchRequest = "{\"jsonrpc\":\"2.0\",\"id\":3,\"method\":\"tools/call\",\"params\":{\"name\":\"search_reflections\",\"arguments\":{}}}";
    var searchResponse = mcp.Handle(searchRequest)!;
    using (var response = JsonDocument.Parse(searchResponse))
    using (var payload = JsonDocument.Parse(response.RootElement.GetProperty("result").GetProperty("content")[0].GetProperty("text").GetString()!))
        Require(payload.RootElement.GetProperty("records").GetArrayLength() == 1, "MCP returns real saved history");
    Require(HasError(mcp.Handle(searchRequest.Replace("{}", "{\"limit\":-1}")), -32602), "MCP validates bounded arguments");
    Require(HasError(mcp.Handle(searchRequest.Replace("{}", "{\"path\":\"../secret\"}")), -32602), "MCP cannot read arbitrary paths");
    Require(HasError(mcp.Handle("not-json"), -32700), "MCP malformed JSON does not crash");
    store.RevokeAgent(grant.Grant.Id);
    Require(HasError(mcp.Handle(searchRequest), -32001), "revocation affects established MCP connection immediately");
    Require(store.DeleteReflection(record.Id) && store.Search().Count == 0, "deleted transcript is no longer retrievable");

    const string addressA = "A1B2C3D4E5F6";
    const string addressB = "112233445566";
    static string Hid(string address) => $@"BTHLEDevice\{{00001812-0000-1000-8000-00805F9B34FB}}_Dev_VID&012717_PID&32b8_REV&00a4_{address}\9&TEST&0&0055";
    var candidates = new[] { new Rc003HostCandidate(Hid(addressA), 100), new Rc003HostCandidate(Hid(addressB), 200) };
    Require(Rc003TargetSelector.SelectForDevice(candidates, addressB)?.HostPid == 200, "second physical remote of same model can be selected");
    var compatible = new Rc003HostCandidate(Hid(addressB).Replace(DeviceSelection.DefaultHardwareToken, transport.HardwareToken), 300);
    Require(Rc003TargetSelector.SelectForDevice([compatible], addressB) is null &&
        Rc003TargetSelector.SelectForDevice([compatible], addressB, transport.HardwareToken)?.HostPid == 300,
        "other HID hardware requires its explicitly declared identifier and selected physical address");
    Require(DeviceSelection.HidAddress(compatible.InstanceId.Replace("00001812", "0000180F"), transport.HardwareToken) is null,
        "matching vendor and address cannot bypass the HID service boundary");
    Require(DeviceSelection.MatchesPhysical($@"BTHLE\DEV_{addressB}\instance", addressB) &&
        !DeviceSelection.MatchesPhysical($@"BTHLE\DEV_{addressA}\instance", addressB), "physical presence follows selected identity");
    Require(Rc003TargetSelector.SelectForDevice(candidates, "66778899AABB") is null &&
        Rc003TargetSelector.SelectForDevice([candidates[0], candidates[1] with { HostPid = 100 }], addressB) is null,
        "unknown identity and shared host fail closed");
    var selectionFile = Path.Combine(root, "selected.json");
    DeviceSelection.Save(new("xiaomi-rc003", addressB), selectionFile);
    Require(DeviceSelection.Read(selectionFile)?.BluetoothAddress == addressB, "selection persists without source edits");
    File.WriteAllText(selectionFile, "{\"ProfileId\":\"../../bad\",\"BluetoothAddress\":\"112233445566\"}");
    Throws(() => DeviceSelection.Read(selectionFile), "untrusted selector cannot inject paths");
    File.WriteAllText(Path.Combine(root, "journal.json"), "broken");
    Throws(() => store.Read(), "storage corruption is visible and not silently overwritten");
    Console.WriteLine("PASS Voice Anything history, privacy, MCP and device contracts");
}
finally { Directory.Delete(root, recursive: true); }

static bool HasError(string? response, int? code = null)
{
    using var document = JsonDocument.Parse(response!);
    return document.RootElement.TryGetProperty("error", out var error) &&
        (code is null || error.GetProperty("code").GetInt32() == code);
}
static void Require(bool value, string message)
{
    if (!value) throw new Exception("FAIL " + message);
    Console.WriteLine("PASS " + message);
}
static void Throws(Action action, string message)
{
    try { action(); } catch (Exception exception) when (exception is ArgumentException or InvalidDataException or JsonException)
    { Console.WriteLine("PASS " + message); return; }
    throw new Exception("FAIL " + message);
}
