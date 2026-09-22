using System.Reflection;

const string contractTypeName = "SayAll.HidBridge.Contracts.Rc003DriverContract";
var assembly = Assembly.Load("SayAll.HidBridge.Contracts");
var contractType = assembly.GetType(contractTypeName);
Require(contractType is not null, $"Missing {contractTypeName}");

var isSupported = contractType!.GetMethod(
    "IsSupported",
    BindingFlags.Public | BindingFlags.Static,
    binder: null,
    types: [typeof(string), typeof(long), typeof(string)],
    modifiers: null);
Require(isSupported is not null, "Missing Rc003DriverContract.IsSupported");

const string expectedHash =
    "D628C3A9C6B34E51D79F91C204A787AD6A598F863BF24395E612FBFD3BBE0B87";
Require(
    Invoke("Microsoft.Bluetooth.Profiles.HidOverGatt.dll", 233_472, expectedHash),
    "The measured Windows 11 HidOverGatt driver must pass the fingerprint gate");
Require(
    !Invoke("other.dll", 233_472, expectedHash),
    "A different module name must fail closed");
Require(
    !Invoke("Microsoft.Bluetooth.Profiles.HidOverGatt.dll", 233_471, expectedHash),
    "A different module size must fail closed");
Require(
    !Invoke(
        "Microsoft.Bluetooth.Profiles.HidOverGatt.dll",
        233_472,
        new string('0', expectedHash.Length)),
    "A different module hash must fail closed");

Console.WriteLine("PASS exact HidOverGatt fingerprint gate");

var isSupportedDevice = contractType.GetMethod(
    "IsSupportedDeviceInstanceId",
    BindingFlags.Public | BindingFlags.Static,
    binder: null,
    types: [typeof(string)],
    modifiers: null);
Require(isSupportedDevice is not null, "Missing Rc003DriverContract.IsSupportedDeviceInstanceId");

const string measuredParentInstanceId =
    "BTHLEDevice\\{00001812-0000-1000-8000-00805F9B34FB}_" +
    "Dev_VID&012717_PID&32b8_REV&00a4_A1B2C3D4E5F6\\9&2b74db0b&0&0055";
Require(
    isSupportedDevice!.Invoke(null, [measuredParentInstanceId]) as bool? == true,
    "The measured RC003MS HID parent must pass the device gate");
Require(
    isSupportedDevice.Invoke(null, [measuredParentInstanceId.Replace("32b8", "32b9")]) as bool? == false,
    "A different product ID must fail closed");
Require(
    isSupportedDevice.Invoke(null, [measuredParentInstanceId.Replace("00a4", "00a5")]) as bool? == false,
    "A different revision must fail closed");
Require(
    isSupportedDevice.Invoke(null, [measuredParentInstanceId.Replace("00001812", "0000180f")]) as bool? == false,
    "A non-HID Bluetooth service must fail closed");

const string measuredPhysicalInstanceId =
    "BTHLE\\DEV_A1B2C3D4E5F6\\8&TEST&0&A1B2C3D4E5F6";
Require(SayAll.HidBridge.Contracts.DeviceSelection.MatchesPhysical(measuredPhysicalInstanceId, "A1B2C3D4E5F6"),
    "Selected physical Bluetooth device must pass presence gate");
Require(!SayAll.HidBridge.Contracts.DeviceSelection.MatchesPhysical(measuredPhysicalInstanceId, "112233445566"),
    "A different selected address must fail the physical-device gate");
Require(!SayAll.HidBridge.Contracts.DeviceSelection.MatchesPhysical(measuredParentInstanceId, "A1B2C3D4E5F6"),
    "A service instance cannot masquerade as the physical device");
var isSupportedHost = contractType.GetMethod(
    "IsSupportedHostProcessName",
    BindingFlags.Public | BindingFlags.Static,
    binder: null,
    types: [typeof(string)],
    modifiers: null);
Require(isSupportedHost is not null, "Missing Rc003DriverContract.IsSupportedHostProcessName");
Require(
    isSupportedHost!.Invoke(null, ["WUDFHost.exe"]) as bool? == true,
    "The dedicated Windows user-mode driver host must pass the host gate");
Require(
    isSupportedHost.Invoke(null, ["explorer.exe"]) as bool? == false,
    "Any non-WUDFHost process must fail closed");

Console.WriteLine("PASS exact RC003MS target identity gates");

var isSupportedCapturePoint = contractType.GetMethod(
    "IsSupportedCapturePoint",
    BindingFlags.Public | BindingFlags.Static,
    binder: null,
    types: [typeof(int), typeof(byte[])],
    modifiers: null);
Require(isSupportedCapturePoint is not null, "Missing Rc003DriverContract.IsSupportedCapturePoint");
var measuredCaptureBytes = Convert.FromHexString("B918000000E826CAFEFF");
Require(
    isSupportedCapturePoint!.Invoke(null, [0x15980, measuredCaptureBytes]) as bool? == true,
    "The disassembled OnReportValueChanged vector capture point must pass");
Require(
    isSupportedCapturePoint.Invoke(null, [0x15981, measuredCaptureBytes]) as bool? == false,
    "A shifted capture address must fail closed");
var changedCaptureBytes = measuredCaptureBytes.ToArray();
changedCaptureBytes[0] ^= 0xFF;
Require(
    isSupportedCapturePoint.Invoke(null, [0x15980, changedCaptureBytes]) as bool? == false,
    "Changed machine code at the capture point must fail closed");

Console.WriteLine("PASS exact OnReportValueChanged capture-point gate");

var isSupportedReportArrivedPoint = contractType.GetMethod(
    "IsSupportedReportArrivedPoint",
    BindingFlags.Public | BindingFlags.Static,
    binder: null,
    types: [typeof(int), typeof(byte[])],
    modifiers: null);
Require(
    isSupportedReportArrivedPoint is not null,
    "Missing Rc003DriverContract.IsSupportedReportArrivedPoint");
var measuredReportArrivedBytes = Convert.FromHexString("4C894424188854241055");
Require(
    isSupportedReportArrivedPoint!.Invoke(null, [0x20720, measuredReportArrivedBytes]) as bool? == true,
    "The disassembled ReportArrived entry point must pass");
Require(
    isSupportedReportArrivedPoint.Invoke(null, [0x20721, measuredReportArrivedBytes]) as bool? == false,
    "A shifted ReportArrived address must fail closed");

Console.WriteLine("PASS exact ReportArrived entry-point gate");

const string gadgetContractTypeName = "SayAll.HidBridge.Contracts.FridaGadgetContract";
var gadgetContractType = assembly.GetType(gadgetContractTypeName);
Require(gadgetContractType is not null, $"Missing {gadgetContractTypeName}");
var isSupportedGadget = gadgetContractType!.GetMethod(
    "IsSupported",
    BindingFlags.Public | BindingFlags.Static,
    binder: null,
    types: [typeof(long), typeof(string)],
    modifiers: null);
Require(isSupportedGadget is not null, "Missing FridaGadgetContract.IsSupported");
const string gadgetHash =
    "6FCA4007B2284C765A6C15C967A741F536B5865BF83867326A54029A3B752748";
Require(
    isSupportedGadget!.Invoke(null, [23_575_552L, gadgetHash]) as bool? == true,
    "The pinned official Frida Gadget must pass its fingerprint gate");
Require(
    isSupportedGadget.Invoke(null, [23_575_553L, gadgetHash]) as bool? == false,
    "A changed Gadget length must fail closed");
Require(
    isSupportedGadget.Invoke(null, [23_575_552L, new string('0', gadgetHash.Length)]) as bool? == false,
    "A changed Gadget hash must fail closed");

Console.WriteLine("PASS pinned Frida Gadget fingerprint gate");

const string codecTypeName = "SayAll.HidBridge.Contracts.HidBridgeMessageCodec";
var codecType = assembly.GetType(codecTypeName);
Require(codecType is not null, $"Missing {codecTypeName}");
var parseMessage = codecType!.GetMethod(
    "ParseGadgetLine",
    BindingFlags.Public | BindingFlags.Static,
    binder: null,
    types: [typeof(string), typeof(string)],
    modifiers: null);
Require(parseMessage is not null, "Missing HidBridgeMessageCodec.ParseGadgetLine");

const string sessionToken =
    "00112233445566778899AABBCCDDEEFF00112233445566778899AABBCCDDEEFF";
var validMessage =
    "{\"version\":1,\"kind\":\"hid_report\",\"token\":\"" + sessionToken +
    "\",\"report_id\":1,\"payload\":\"2800F1000000\"}";
var parsedMessage = parseMessage!.Invoke(null, [validMessage, sessionToken]);
Require(parsedMessage is not null, "A valid authenticated HID report must parse");
RequireProperty(parsedMessage!, "ReportId", (byte)1);
RequireBytes(
    parsedMessage!.GetType().GetProperty("Payload")?.GetValue(parsedMessage),
    0x28, 0x00, 0xF1, 0x00, 0x00, 0x00);

var paddedDriverMessage = validMessage.Replace("2800F1000000", "5200000000000000");
var parsedPaddedDriverMessage = parseMessage.Invoke(null, [paddedDriverMessage, sessionToken]);
Require(parsedPaddedDriverMessage is not null, "An eight-byte driver report with zero padding must normalize");
RequireBytes(
    parsedPaddedDriverMessage!.GetType().GetProperty("Payload")?.GetValue(parsedPaddedDriverMessage),
    0x52, 0x00, 0x00, 0x00, 0x00, 0x00);

var prefixedDriverMessage = validMessage.Replace("2800F1000000", "0152000000000000");
var parsedPrefixedDriverMessage = parseMessage.Invoke(null, [prefixedDriverMessage, sessionToken]);
Require(parsedPrefixedDriverMessage is not null, "An eight-byte report-ID-prefixed driver report must normalize");
RequireBytes(
    parsedPrefixedDriverMessage!.GetType().GetProperty("Payload")?.GetValue(parsedPrefixedDriverMessage),
    0x52, 0x00, 0x00, 0x00, 0x00, 0x00);

Require(
    parseMessage.Invoke(
        null,
        [validMessage.Replace("2800F1000000", "52000000000000FF"), sessionToken]) is null,
    "An eight-byte driver report without a recognized padding layout must fail closed");

Require(
    parseMessage.Invoke(null, [validMessage, new string('F', sessionToken.Length)]) is null,
    "A report with the wrong session token must be rejected");
Require(
    parseMessage.Invoke(null, [validMessage.Replace("\"version\":1", "\"version\":2"), sessionToken]) is null,
    "An unknown IPC protocol version must be rejected");
Require(
    parseMessage.Invoke(null, [validMessage.Replace("2800F1000000", "2800"), sessionToken]) is null,
    "A report with a non-six-byte payload must be rejected");
Require(
    parseMessage.Invoke(null, [validMessage.Replace("\"report_id\":1", "\"report_id\":2"), sessionToken]) is null,
    "A non-keyboard report ID must be rejected");
Require(
    parseMessage.Invoke(null, ["not-json", sessionToken]) is null,
    "Malformed helper input must be rejected without throwing");

Console.WriteLine("PASS authenticated versioned HID IPC schema");

var parseProbe = codecType.GetMethod(
    "ParseProbeLine",
    BindingFlags.Public | BindingFlags.Static,
    binder: null,
    types: [typeof(string), typeof(string)],
    modifiers: null);
Require(parseProbe is not null, "Missing HidBridgeMessageCodec.ParseProbeLine");
var probeLine =
    "{\"version\":1,\"kind\":\"hook_probe\",\"token\":\"" + sessionToken +
    "\",\"source\":\"report_arrived\",\"report_id\":1,\"payload_length\":6," +
    "\"reason\":\"accepted\"}";
var parsedProbe = parseProbe!.Invoke(null, [probeLine, sessionToken]);
Require(parsedProbe is not null, "An authenticated bounded hook probe must parse");
RequireProperty(parsedProbe!, "Source", "report_arrived");
RequireProperty(parsedProbe!, "ReportId", (byte)1);
RequireProperty(parsedProbe!, "PayloadLength", 6);
RequireProperty(parsedProbe!, "Reason", "accepted");
Require(
    parseProbe.Invoke(null, [probeLine, new string('F', sessionToken.Length)]) is null,
    "A hook probe with the wrong token must be rejected");
Require(
    parseProbe.Invoke(null, [probeLine.Replace("\"payload_length\":6", "\"payload_length\":1024"), sessionToken]) is null,
    "An unbounded hook probe payload length must be rejected");

Console.WriteLine("PASS authenticated bounded hook diagnostics");

const string scriptBuilderTypeName = "SayAll.HidBridge.Contracts.GadgetScriptBuilder";
var scriptBuilderType = assembly.GetType(scriptBuilderTypeName);
Require(scriptBuilderType is not null, $"Missing {scriptBuilderTypeName}");
var buildScript = scriptBuilderType!.GetMethod(
    "Build",
    BindingFlags.Public | BindingFlags.Static,
    binder: null,
    types: [typeof(int), typeof(string)],
    modifiers: null);
Require(buildScript is not null, "Missing GadgetScriptBuilder.Build");
var script = buildScript!.Invoke(null, [30_684, sessionToken]) as string;
Require(!string.IsNullOrWhiteSpace(script), "Gadget script must not be empty");
Require(script!.Contains("127.0.0.1", StringComparison.Ordinal), "Gadget must use loopback only");
Require(script.Contains("30684", StringComparison.Ordinal), "Gadget must use the selected ephemeral port");
Require(script.Contains(sessionToken, StringComparison.Ordinal), "Gadget messages must carry the session token");
Require(script.Contains("0x15980", StringComparison.OrdinalIgnoreCase), "Gadget must hook the measured capture RVA");
Require(script.Contains("b918000000e826cafeff", StringComparison.OrdinalIgnoreCase), "Gadget must verify capture bytes before hooking");
Require(script.Contains("this.context.rdi", StringComparison.Ordinal), "Gadget must read the completed report vector");
Require(script.Contains("this.context.r15", StringComparison.Ordinal), "Gadget must read the report ID register");
Require(script.Contains("0x20720", StringComparison.OrdinalIgnoreCase), "Gadget must hook the measured ReportArrived RVA");
Require(script.Contains("4c894424188854241055", StringComparison.OrdinalIgnoreCase), "Gadget must verify ReportArrived bytes before hooking");
Require(script.Contains("this.context.rdx", StringComparison.Ordinal), "Gadget must read the ReportArrived report ID");
Require(script.Contains("this.context.r8", StringComparison.Ordinal), "Gadget must read the ReportArrived owner");
Require(
    script.Contains("length === 6 || length === 7 || length === 8", StringComparison.Ordinal),
    "Gadget must forward only the observed bounded RC003 report lengths");
var forwardedPayloadIndex = script.IndexOf(
    "payload: hex(inspected.begin, inspected.length)",
    StringComparison.Ordinal);
var nativeSuppressionIndex = script.IndexOf(
    "inspected.begin.writeByteArray(new Uint8Array(inspected.length))",
    StringComparison.Ordinal);
Require(
    forwardedPayloadIndex >= 0 && nativeSuppressionIndex > forwardedPayloadIndex,
    "Gadget must copy each RC003MS report before suppressing its native Windows keyboard event");
Require(CountOccurrences(script, "Interceptor.attach") == 2, "Gadget must install exactly the two measured hooks");
Require(!script.Contains("NtDeviceIoControlFile", StringComparison.Ordinal), "Old IOCTL hooks must not return");
Require(!script.Contains("0x80018483", StringComparison.OrdinalIgnoreCase), "Old IOCTL guesses must not return");

Console.WriteLine("PASS single-purpose hash-gated Gadget script suppresses native RC003MS input");

const string targetSelectorTypeName = "SayAll.HidBridge.Contracts.Rc003TargetSelector";
var targetSelectorType = assembly.GetType(targetSelectorTypeName);
Require(targetSelectorType is not null, $"Missing {targetSelectorTypeName}");
var candidateType = assembly.GetType("SayAll.HidBridge.Contracts.Rc003HostCandidate");
Require(candidateType is not null, "Missing SayAll.HidBridge.Contracts.Rc003HostCandidate");
var selectTarget = targetSelectorType!.GetMethod("SelectSingleSupportedHost");
Require(selectTarget is not null, "Missing Rc003TargetSelector.SelectSingleSupportedHost");

var supportedCandidate = Activator.CreateInstance(candidateType!, measuredParentInstanceId, 32_588);
var unsupportedCandidate = Activator.CreateInstance(
    candidateType!,
    measuredParentInstanceId.Replace("32b8", "32b9"),
    10_000);
Require(supportedCandidate is not null && unsupportedCandidate is not null, "Cannot create host candidates");

var oneSupported = Array.CreateInstance(candidateType!, 2);
oneSupported.SetValue(unsupportedCandidate, 0);
oneSupported.SetValue(supportedCandidate, 1);
var selectedTarget = selectTarget!.Invoke(null, [oneSupported]);
Require(selectedTarget is not null, "The unique measured RC003MS host must be selected");
RequireProperty(selectedTarget!, "HostPid", 32_588);
RequireProperty(selectedTarget!, "InstanceId", measuredParentInstanceId);

var duplicateSupported = Array.CreateInstance(candidateType!, 2);
duplicateSupported.SetValue(supportedCandidate, 0);
duplicateSupported.SetValue(Activator.CreateInstance(candidateType!, measuredParentInstanceId, 32_589), 1);
Require(
    selectTarget.Invoke(null, [duplicateSupported]) is null,
    "More than one supported host must fail closed");

var invalidPid = Array.CreateInstance(candidateType!, 1);
invalidPid.SetValue(Activator.CreateInstance(candidateType!, measuredParentInstanceId, 0), 0);
Require(
    selectTarget.Invoke(null, [invalidPid]) is null,
    "A stale or missing WUDF host PID must fail closed");

Console.WriteLine("PASS unique RC003MS WUDF host selection");

var parseReady = codecType.GetMethod(
    "ParseReadyLine",
    BindingFlags.Public | BindingFlags.Static,
    binder: null,
    types: [typeof(string), typeof(string)],
    modifiers: null);
Require(parseReady is not null, "Missing HidBridgeMessageCodec.ParseReadyLine");
var readyLine =
    "{\"version\":1,\"kind\":\"ready\",\"token\":\"" + sessionToken +
    "\",\"hook_installed\":true}";
Require(
    parseReady!.Invoke(null, [readyLine, sessionToken]) as bool? == true,
    "An authenticated installed ready message must parse");
Require(
    parseReady.Invoke(null, [readyLine.Replace("true", "false"), sessionToken]) as bool? == false,
    "A ready message must preserve the not-yet-installed state");
Require(
    parseReady.Invoke(null, [readyLine, new string('F', sessionToken.Length)]) is null,
    "A ready message with the wrong session token must be rejected");

Console.WriteLine("PASS authenticated helper readiness state");

const string runtimeConfigTypeName = "SayAll.HidBridge.Contracts.GadgetRuntimeConfigBuilder";
var runtimeConfigType = assembly.GetType(runtimeConfigTypeName);
Require(runtimeConfigType is not null, $"Missing {runtimeConfigTypeName}");
var buildRuntimeConfig = runtimeConfigType!.GetMethod("Build", BindingFlags.Public | BindingFlags.Static);
Require(buildRuntimeConfig is not null, "Missing GadgetRuntimeConfigBuilder.Build");
var runtimeConfig = buildRuntimeConfig!.Invoke(null, null) as string;
Require(!string.IsNullOrWhiteSpace(runtimeConfig), "Gadget runtime config must not be empty");
using (var runtimeDocument = System.Text.Json.JsonDocument.Parse(runtimeConfig!))
{
    var root = runtimeDocument.RootElement;
    Require(root.GetProperty("runtime").GetString() == "qjs", "Gadget must use the bounded QJS runtime");
    Require(root.GetProperty("teardown").GetString() == "minimal", "Gadget teardown must not block WUDFHost");
    var interaction = root.GetProperty("interaction");
    Require(interaction.GetProperty("type").GetString() == "script", "Gadget must load only the generated script");
    var scriptFileName = runtimeConfigType.GetField("ScriptFileName")?.GetRawConstantValue() as string;
    Require(
        interaction.GetProperty("path").GetString() == scriptFileName,
        "Gadget config and script filename must match");
    Require(interaction.GetProperty("on_change").GetString() == "ignore", "Runtime files must not hot-reload");
}

Console.WriteLine("PASS minimal Frida Gadget runtime config");

const string logCodecTypeName = "SayAll.HidBridge.Contracts.HidBridgeLogEventCodec";
var logCodecType = assembly.GetType(logCodecTypeName);
Require(logCodecType is not null, $"Missing {logCodecTypeName}");
var parseLogLine = logCodecType!.GetMethod(
    "ParseLine",
    BindingFlags.Public | BindingFlags.Static,
    binder: null,
    types: [typeof(string)],
    modifiers: null);
Require(parseLogLine is not null, "Missing HidBridgeLogEventCodec.ParseLine");
var parsedReadyLog = parseLogLine!.Invoke(
    null,
    ["{\"timestamp\":\"2026-08-26T16:00:00+08:00\",\"event\":\"hook_ready\",\"detail\":null}"]);
Require(parsedReadyLog is not null, "A helper hook-ready log event must parse");
RequireProperty(parsedReadyLog!, "Kind", "hook_ready");

var parsedReportLog = parseLogLine.Invoke(
    null,
    ["{\"timestamp\":\"2026-08-26T16:00:01+08:00\",\"event\":\"hid_report\"," +
     "\"detail\":{\"report_id\":1,\"payload\":\"520000000000\"}}"]);
Require(parsedReportLog is not null, "A helper HID report log event must parse");
RequireProperty(parsedReportLog!, "Kind", "hid_report");
RequireProperty(parsedReportLog!, "ReportId", (byte)1);
RequireProperty(
    parsedReportLog!,
    "Timestamp",
    new DateTimeOffset(2026, 8, 26, 16, 0, 1, TimeSpan.FromHours(8)));
RequireBytes(parsedReportLog!.GetType().GetProperty("Payload")?.GetValue(parsedReportLog),
    0x52, 0x00, 0x00, 0x00, 0x00, 0x00);

Require(
    parseLogLine.Invoke(null, ["{\"event\":\"hid_report\",\"detail\":{\"report_id\":2,\"payload\":\"5200\"}}"])
        is null,
    "Malformed helper report logs must be rejected");
Require(
    parseLogLine.Invoke(null, ["{\"event\":\"untrusted\",\"detail\":null}"]) is null,
    "Unknown helper log events must be ignored");

var initialLogCursor = logCodecType.GetMethod(
    "GetInitialCursor",
    BindingFlags.Public | BindingFlags.Static,
    binder: null,
    types: [typeof(string)],
    modifiers: null);
Require(initialLogCursor is not null, "Missing HidBridgeLogEventCodec.GetInitialCursor");
Require(
    initialLogCursor!.Invoke(null, [
        "{\"event\":\"hook_ready\"}\r\n{\"event\":\"hid_report\"}\r\n"
    ]) as int? == 2,
    "A restarted UI must place its cursor after all prior bridge log events");

Console.WriteLine("PASS helper log to UI status contract");

const string startupPolicyTypeName = "SayAll.HidBridge.Contracts.HidBridgeStartupPolicy";
var startupPolicyType = assembly.GetType(startupPolicyTypeName);
Require(startupPolicyType is not null, $"Missing {startupPolicyTypeName}");
RequireProperty(startupPolicyType!, "GadgetConnectTimeoutSeconds", 5, staticProperty: true);
RequireProperty(startupPolicyType!, "MaximumInjectionAttempts", 2, staticProperty: true);
var shouldRestart = startupPolicyType!.GetMethod(
    "ShouldRestartDeviceAfterTimeout",
    BindingFlags.Public | BindingFlags.Static,
    binder: null,
    types: [typeof(int)],
    modifiers: null);
Require(shouldRestart is not null, "Missing HidBridgeStartupPolicy.ShouldRestartDeviceAfterTimeout");
Require(
    shouldRestart!.Invoke(null, [1]) as bool? == true,
    "The first connection timeout must trigger one controlled RC003MS restart");
Require(
    shouldRestart.Invoke(null, [2]) as bool? == false,
    "The second connection timeout must fail fast instead of restarting again");

Console.WriteLine("PASS bounded HID bridge startup recovery");

const string serviceContractTypeName = "SayAll.HidBridge.Contracts.HidBridgeServiceContract";
var serviceContractType = assembly.GetType(serviceContractTypeName);
Require(serviceContractType is not null, $"Missing {serviceContractTypeName}");
RequireProperty(serviceContractType!, "ProductName", "VoiceAnything", staticProperty: true);
RequireProperty(serviceContractType!, "ServiceName", "VoiceAnythingHidBridge", staticProperty: true);
RequireProperty(serviceContractType!, "DisplayName", "VoiceAnything RC003MS Button Bridge", staticProperty: true);
RequireProperty(serviceContractType!, "LegacyServiceName", "SayAllHidBridge", staticProperty: true);
var sharedLogPath = serviceContractType!.GetProperty(
    "SharedEventLogPath",
    BindingFlags.Public | BindingFlags.Static)?.GetValue(null) as string;
Require(
    sharedLogPath is not null &&
    Path.IsPathFullyQualified(sharedLogPath) &&
    sharedLogPath.Contains(
        $"{Path.DirectorySeparatorChar}VoiceAnything{Path.DirectorySeparatorChar}",
        StringComparison.OrdinalIgnoreCase) &&
    sharedLogPath.StartsWith(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        StringComparison.OrdinalIgnoreCase),
    "The background service must expose one fixed ProgramData event log to the app");

Console.WriteLine("PASS no-popup Windows service identity and machine-wide endpoint");

bool Invoke(string fileName, long length, string sha256)
{
    return isSupported!.Invoke(null, [fileName, length, sha256]) as bool? ?? false;
}

static void Require(bool condition, string message)
{
    if (!condition)
    {
        Console.Error.WriteLine($"FAIL {message}");
        Environment.Exit(1);
    }
}

static void RequireProperty<T>(object target, string name, T expected, bool staticProperty = false)
{
    var targetType = staticProperty && target is Type type ? type : target.GetType();
    var property = targetType.GetProperty(
        name,
        BindingFlags.Public | (staticProperty ? BindingFlags.Static : BindingFlags.Instance));
    Require(property is not null, $"Missing {targetType.Name}.{name}");
    var actual = property!.GetValue(staticProperty ? null : target);
    Require(
        Equals(actual, expected),
        $"Expected {targetType.Name}.{name}={expected}, got {actual}");
}

static void RequireBytes(object? value, params byte[] expected)
{
    Require(value is byte[], "Expected a byte array");
    var actual = (byte[])value!;
    Require(
        actual.SequenceEqual(expected),
        $"Expected bytes {Convert.ToHexString(expected)}, got {Convert.ToHexString(actual)}");
}

static int CountOccurrences(string value, string needle)
{
    var count = 0;
    var offset = 0;
    while ((offset = value.IndexOf(needle, offset, StringComparison.Ordinal)) >= 0)
    {
        count++;
        offset += needle.Length;
    }

    return count;
}
