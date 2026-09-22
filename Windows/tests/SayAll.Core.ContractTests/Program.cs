using System.Reflection;

const string policyTypeName = "SayAll.Core.Onboarding.OnboardingFlowPolicy";
const string capabilitiesTypeName = "SayAll.Core.Onboarding.OnboardingCapabilities";
const string stepTypeName = "SayAll.Core.Onboarding.OnboardingStep";

var coreAssembly = Assembly.Load("SayAll.Core");
var policyType = coreAssembly.GetType(policyTypeName);
Require(policyType is not null, $"Missing {policyTypeName}");

var capabilitiesType = coreAssembly.GetType(capabilitiesTypeName);
Require(capabilitiesType is not null, $"Missing {capabilitiesTypeName}");

var stepType = coreAssembly.GetType(stepTypeName);
Require(stepType is not null, $"Missing {stepTypeName}");

var canContinue = policyType!.GetMethod(
    "CanContinue",
    BindingFlags.Public | BindingFlags.Static,
    binder: null,
    types: [stepType!, capabilitiesType!],
    modifiers: null);
Require(canContinue is not null, "Missing OnboardingFlowPolicy.CanContinue");

var remoteStep = RequiredEnumValue(stepType!, "Remote");

Require(
    InvokeCanContinue(canContinue!, remoteStep, capabilitiesType!,
        ("RemoteConnected", false),
        ("RemoteButtonObserved", false)) is false,
    "Remote setup must not continue before connection or a real button report");
Require(
    InvokeCanContinue(canContinue!, remoteStep, capabilitiesType!,
        ("RemoteConnected", true),
        ("RemoteButtonObserved", false)) is false,
    "Remote setup must not continue on connection status alone");
Require(
    InvokeCanContinue(canContinue!, remoteStep, capabilitiesType!,
        ("RemoteConnected", false),
        ("RemoteButtonObserved", true)) is false,
    "Remote setup must not continue on a button report without connection");
Require(
    InvokeCanContinue(canContinue!, remoteStep, capabilitiesType!,
        ("RemoteConnected", true),
        ("RemoteButtonObserved", true)),
    "Remote setup must continue after connection and a real button report");

Console.WriteLine("PASS onboarding remote gate");

var audioStep = RequiredEnumValue(stepType!, "Audio");
Require(
    InvokeCanContinue(canContinue!, audioStep, capabilitiesType!,
        ("AudioOutputSelected", true),
        ("AudioReady", false)) is false,
    "Audio setup must not continue when the selected output is not ready");
Require(
    InvokeCanContinue(canContinue!, audioStep, capabilitiesType!,
        ("AudioOutputSelected", false),
        ("AudioReady", true)) is false,
    "Audio setup must not continue when no output is selected");
Require(
    InvokeCanContinue(canContinue!, audioStep, capabilitiesType!,
        ("AudioOutputSelected", true),
        ("AudioReady", true)) is false,
    "Audio setup must not continue before a real voice session completes");
Require(
    InvokeCanContinue(canContinue!, audioStep, capabilitiesType!,
        ("AudioOutputSelected", true),
        ("AudioReady", true),
        ("VoiceSessionStarted", true),
        ("VoiceSamplesReceived", true),
        ("VoiceSessionEnded", true)),
    "Audio setup must continue after the selected output and a real voice session are ready");

Console.WriteLine("PASS onboarding audio gate");

var voiceTestStep = RequiredEnumValue(stepType!, "VoiceTest");
var completeVoiceJourney = new (string Name, object Value)[]
{
    ("VoiceSessionStarted", true),
    ("VoiceSamplesReceived", true),
    ("VoiceSessionEnded", true),
    ("TranscriptionAppeared", true),
    ("ManualTranscriptInputObserved", false),
};
Require(
    InvokeCanContinue(canContinue!, voiceTestStep, capabilitiesType!,
        ("VoiceSessionStarted", true),
        ("VoiceSamplesReceived", false),
        ("VoiceSessionEnded", true),
        ("TranscriptionAppeared", true),
        ("ManualTranscriptInputObserved", false)) is false,
    "Voice test must not continue before real microphone samples arrive");
Require(
    InvokeCanContinue(canContinue!, voiceTestStep, capabilitiesType!, completeVoiceJourney),
    "Voice test must continue after the full start-samples-stop-transcription journey");
Require(
    InvokeCanContinue(canContinue!, voiceTestStep, capabilitiesType!,
        ("VoiceSessionStarted", true),
        ("VoiceSamplesReceived", true),
        ("VoiceSessionEnded", true),
        ("VoicePlaybackOpened", true),
        ("VoicePlaybackConfirmed", false)) is false,
    "Opening playback alone must not confirm the captured voice");
Require(
    InvokeCanContinue(canContinue!, voiceTestStep, capabilitiesType!,
        ("VoiceSessionStarted", true),
        ("VoiceSamplesReceived", true),
        ("VoiceSessionEnded", true),
        ("VoicePlaybackOpened", true),
        ("VoicePlaybackConfirmed", true)),
    "Windows playback confirmation must validate a complete real voice journey");
Require(
    InvokeCanContinue(canContinue!, voiceTestStep, capabilitiesType!,
        ("VoiceSessionStarted", true),
        ("VoiceSamplesReceived", true),
        ("VoiceSessionEnded", true),
        ("TranscriptionAppeared", true),
        ("ManualTranscriptInputObserved", true)) is false,
    "Manually typed text must not satisfy the remote voice test");

Console.WriteLine("PASS onboarding voice journey gate");

var controlsStep = RequiredEnumValue(stepType!, "Controls");
Require(
    InvokeCanContinue(canContinue!, controlsStep, capabilitiesType!,
        ("TestedRemoteButtonCount", 0)),
    "Saving button bindings must not be blocked by optional hardware verification");
Require(
    InvokeCanContinue(canContinue!, controlsStep, capabilitiesType!,
        ("TestedRemoteButtonCount", 3)),
    "Saving button bindings must remain available after hardware verification");

Console.WriteLine("PASS button bindings can always be saved while hardware verification stays optional");

const string hidParserTypeName = "SayAll.Core.Input.RemoteHidReportParser";
var hidParserType = coreAssembly.GetType(hidParserTypeName);
Require(hidParserType is not null, $"Missing {hidParserTypeName}");
var parseUsages = hidParserType!.GetMethod(
    "ParseUsages",
    BindingFlags.Public | BindingFlags.Static,
    binder: null,
    types: [typeof(uint), typeof(byte[])],
    modifiers: null);
Require(parseUsages is not null, "Missing RemoteHidReportParser.ParseUsages");

RequireUsageSet(
    parseUsages!.Invoke(null, [1u, new byte[] { 0x28, 0x00, 0xF1, 0x00, 0x00, 0x00 }]),
    0x0028,
    0x00F1);
RequireUsageSet(
    parseUsages.Invoke(null, [1u, new byte[] { 0x01, 0x80, 0x00, 0x81, 0x00, 0x00, 0x00 }]),
    0x0080,
    0x0081);
Require(
    parseUsages.Invoke(null, [2u, new byte[] { 0x28, 0x00 }]) is null,
    "HID parser must reject non-keyboard report IDs");
Require(
    parseUsages.Invoke(null, [1u, new byte[] { 0x28 }]) is null,
    "HID parser must reject incomplete usage words");

Console.WriteLine("PASS RC003 HID report parser");

const string buttonCatalogTypeName = "SayAll.Core.Input.RemoteButtonCatalog";
var buttonCatalogType = coreAssembly.GetType(buttonCatalogTypeName);
Require(buttonCatalogType is not null, $"Missing {buttonCatalogTypeName}");
var fromUsage = buttonCatalogType!.GetMethod(
    "FromUsage",
    BindingFlags.Public | BindingFlags.Static,
    binder: null,
    types: [typeof(ushort)],
    modifiers: null);
Require(fromUsage is not null, "Missing RemoteButtonCatalog.FromUsage");

var expectedButtons = new (ushort Usage, string Button)[]
{
    (0x0066, "Power"),
    (0x0052, "Up"),
    (0x0050, "Left"),
    (0x0028, "Ok"),
    (0x004F, "Right"),
    (0x0051, "Down"),
    (0x00F1, "Back"),
    (0x0080, "VolumeUp"),
    (0x004A, "Home"),
    (0x0081, "VolumeDown"),
    (0x0065, "Menu"),
    (0x0035, "Tv"),
};
foreach (var (usage, button) in expectedButtons)
{
    Require(
        fromUsage!.Invoke(null, [usage])?.ToString() == button,
        $"RC003 usage 0x{usage:X4} must map to {button}");
}
Require(
    fromUsage!.Invoke(null, [(ushort)0xFFFF]) is null,
    "Unknown RC003 usages must remain unmapped");

Console.WriteLine("PASS RC003 button usage catalog");

const string capabilitiesParserTypeName = "SayAll.Core.Audio.AtvvCapabilities";
var atvvCapabilitiesType = coreAssembly.GetType(capabilitiesParserTypeName);
Require(atvvCapabilitiesType is not null, $"Missing {capabilitiesParserTypeName}");
var parseCapabilities = atvvCapabilitiesType!.GetMethod(
    "Parse",
    BindingFlags.Public | BindingFlags.Static,
    binder: null,
    types: [typeof(byte[])],
    modifiers: null);
Require(parseCapabilities is not null, "Missing AtvvCapabilities.Parse");

var observedRc003Capabilities = parseCapabilities!.Invoke(
    null,
    [new byte[] { 0x0B, 0x01, 0x00, 0x02, 0x03, 0x00, 0x78 }]);
Require(observedRc003Capabilities is not null, "Observed RC003MS capability packet must parse");
RequireProperty(observedRc003Capabilities!, "Version", (ushort)0x0100);
RequireProperty(observedRc003Capabilities!, "Codecs", (byte)0x02);
RequireProperty(observedRc003Capabilities!, "Interaction", (byte)0x03);
RequireProperty(observedRc003Capabilities!, "FrameSize", 120);
RequireProperty(observedRc003Capabilities!, "SelectedCodec", (byte)0x02);
RequireProperty(observedRc003Capabilities!, "SampleRate", 16_000d);
Require(
    parseCapabilities.Invoke(null, [new byte[] { 0x0B, 0x01 }]) is null,
    "Truncated ATVV capabilities must be rejected");

Console.WriteLine("PASS observed RC003MS ATVV capabilities");

const string controlPacketTypeName = "SayAll.Core.Audio.AtvvControlPacket";
var controlPacketType = coreAssembly.GetType(controlPacketTypeName);
Require(controlPacketType is not null, $"Missing {controlPacketTypeName}");
var parseControlPacket = controlPacketType!.GetMethod(
    "Parse",
    BindingFlags.Public | BindingFlags.Static,
    binder: null,
    types: [typeof(byte[])],
    modifiers: null);
Require(parseControlPacket is not null, "Missing AtvvControlPacket.Parse");

var remoteOpenPacket = parseControlPacket!.Invoke(null, [new byte[] { 0x08 }]);
Require(remoteOpenPacket is not null, "Remote microphone request must parse");
Require(
    remoteOpenPacket!.GetType().GetProperty("Kind")?.GetValue(remoteOpenPacket)?.ToString() ==
        "RemoteMicrophoneRequested",
    "Opcode 0x08 must route to a remote microphone request");

var streamStartPacket = parseControlPacket.Invoke(
    null,
    [new byte[] { 0x04, 0x03, 0x02, 0x07 }]);
Require(streamStartPacket is not null, "ATVV stream-start packet must parse");
Require(
    streamStartPacket!.GetType().GetProperty("Kind")?.GetValue(streamStartPacket)?.ToString() ==
        "StreamStarted",
    "Opcode 0x04 must route to stream start");
RequireProperty(streamStartPacket!, "Codec", (byte)0x02);
RequireProperty(streamStartPacket!, "SessionId", (byte)0x07);

var syncPacket = parseControlPacket.Invoke(
    null,
    [new byte[] { 0x0A, 0x00, 0x00, 0x00, 0xFF, 0x9C, 0x12 }]);
Require(syncPacket is not null, "ATVV decoder sync packet must parse");
Require(
    syncPacket!.GetType().GetProperty("Kind")?.GetValue(syncPacket)?.ToString() ==
        "DecoderSynchronized",
    "Opcode 0x0A must route to decoder synchronization");
RequireProperty(syncPacket!, "Predictor", -100);
RequireProperty(syncPacket!, "StepIndex", 18);

var streamStopPacket = parseControlPacket.Invoke(null, [new byte[] { 0x00 }]);
Require(streamStopPacket is not null, "ATVV stream-stop packet must parse");
Require(
    streamStopPacket!.GetType().GetProperty("Kind")?.GetValue(streamStopPacket)?.ToString() ==
        "StreamStopped",
    "Opcode 0x00 must route to stream stop");
Require(
    parseControlPacket.Invoke(null, [new byte[] { 0x0A, 0x00 }]) is null,
    "Truncated ATVV decoder sync packets must be rejected");

Console.WriteLine("PASS RC003MS ATVV control routing");

const string atvvProtocolTypeName = "SayAll.Core.Audio.AtvvProtocol";
var atvvProtocolType = coreAssembly.GetType(atvvProtocolTypeName);
Require(atvvProtocolType is not null, $"Missing {atvvProtocolTypeName}");
RequireBytes(
    InvokeStatic(atvvProtocolType!, "GetCapabilitiesV10"),
    0x0A, 0x01, 0x00, 0x00, 0x03, 0x03);
RequireBytes(
    InvokeStatic(atvvProtocolType!, "MicrophoneOpen", (ushort)0x0100, (byte)0x02),
    0x0C, 0x00);
RequireBytes(
    InvokeStatic(atvvProtocolType!, "MicrophoneOpen", (ushort)0x0001, (byte)0x02),
    0x0C, 0x00, 0x02);
RequireBytes(
    InvokeStatic(atvvProtocolType!, "MicrophoneClose", (ushort)0x0100, (byte)0x07),
    0x0D, 0x07);
RequireBytes(
    InvokeStatic(atvvProtocolType!, "MicrophoneClose", (ushort)0x0001, (byte)0x07),
    0x0D);
RequireBytes(
    InvokeStatic(atvvProtocolType!, "MicrophoneExtend", (ushort)0x0100, (byte)0x07),
    0x0E, 0x07);
Require(
    InvokeStatic(atvvProtocolType!, "MicrophoneExtend", (ushort)0x0001, (byte)0x07) is null,
    "ATVV v1.0 must not emit unsupported microphone extension commands");
Require(
    InvokeStatic(atvvProtocolType!, "SupportsAudio", 16_000d) as bool? == true,
    "RC003MS 16 kHz audio must be accepted");
Require(
    InvokeStatic(atvvProtocolType!, "SupportsAudio", 8_000d) as bool? == false,
    "8 kHz audio must fail closed");

Console.WriteLine("PASS RC003MS ATVV command contract");

const string voiceDecoderTypeName = "SayAll.Core.Audio.AtvvVoiceDecoder";
var voiceDecoderType = coreAssembly.GetType(voiceDecoderTypeName);
Require(voiceDecoderType is not null, $"Missing {voiceDecoderTypeName}");
var voiceDecoder = Activator.CreateInstance(voiceDecoderType!, [120]);
Require(voiceDecoder is not null, "Could not create ATVV voice decoder");
var applyControl = voiceDecoderType!.GetMethod("ApplyControl", [controlPacketType!]);
var appendAudio = voiceDecoderType.GetMethod("AppendAudio", [typeof(byte[])]);
Require(applyControl is not null, "Missing AtvvVoiceDecoder.ApplyControl");
Require(appendAudio is not null, "Missing AtvvVoiceDecoder.AppendAudio");

applyControl!.Invoke(voiceDecoder, [streamStartPacket]);
applyControl.Invoke(voiceDecoder, [syncPacket]);
RequireFramesAsPcm(
    appendAudio!.Invoke(voiceDecoder, [Enumerable.Repeat((byte)0x00, 60).ToArray()]));
RequireSinglePcmFrame(
    appendAudio.Invoke(voiceDecoder, [Enumerable.Repeat((byte)0x00, 60).ToArray()]),
    expectedLength: 240,
    expectedFirstSample: -95);
RequireProperty(voiceDecoder!, "IsStreaming", true);

applyControl.Invoke(voiceDecoder, [streamStopPacket]);
RequireProperty(voiceDecoder!, "IsStreaming", false);
RequireFramesAsPcm(
    appendAudio.Invoke(voiceDecoder, [Enumerable.Repeat((byte)0x00, 120).ToArray()]));

Console.WriteLine("PASS RC003MS ATVV voice session framing");

const string decoderTypeName = "SayAll.Core.Audio.ImaAdpcmDecoder";
var decoderType = coreAssembly.GetType(decoderTypeName);
Require(decoderType is not null, $"Missing {decoderTypeName}");
var decoder = Activator.CreateInstance(decoderType!);
Require(decoder is not null, "Could not create IMA ADPCM decoder");
var decode = decoderType!.GetMethod("Decode", [typeof(byte[])]);
var reset = decoderType.GetMethod("Reset", [typeof(int), typeof(int)]);
Require(decode is not null, "Missing ImaAdpcmDecoder.Decode");
Require(reset is not null, "Missing ImaAdpcmDecoder.Reset");
RequireShorts(decode!.Invoke(decoder, [new byte[] { 0x11 }]), 1, 2);
reset!.Invoke(decoder, [0, 0]);
RequireShorts(decode.Invoke(decoder, [new byte[] { 0x7F }]), 11, -19);
reset.Invoke(decoder, [100_000, 1_000]);
RequireProperty(decoder!, "Predictor", 32_767);
RequireProperty(decoder!, "StepIndex", 88);

Console.WriteLine("PASS RC003MS high-nibble-first IMA ADPCM decoder");

const string postprocessorTypeName = "SayAll.Core.Audio.PcmPostprocessor";
var postprocessorType = coreAssembly.GetType(postprocessorTypeName);
Require(postprocessorType is not null, $"Missing {postprocessorTypeName}");
RequireShorts(
    InvokeStatic(postprocessorType!, "Process", new short[] { 0, 1_000, 0 }, 0d),
    0, 500, 0);
RequireShorts(
    InvokeStatic(postprocessorType!, "Process", new short[] { 20_000 }, 24d),
    short.MaxValue);
RequireShorts(
    InvokeStatic(postprocessorType!, "Process", new short[] { 20_000 }, double.PositiveInfinity),
    20_000);

Console.WriteLine("PASS upstream PCM smoothing and gain clamp");

const string captureBufferTypeName = "SayAll.Core.Audio.PcmCaptureBuffer";
var captureBufferType = coreAssembly.GetType(captureBufferTypeName);
Require(captureBufferType is not null, $"Missing {captureBufferTypeName}");
var captureBuffer = Activator.CreateInstance(captureBufferType!);
Require(captureBuffer is not null, "Could not create PCM capture buffer");
var appendCapture = captureBufferType!.GetMethod("Append", [typeof(short[])]);
var resetCapture = captureBufferType.GetMethod("Reset", Type.EmptyTypes);
var snapshotCapture = captureBufferType.GetMethod("Snapshot", Type.EmptyTypes);
var durationSeconds = captureBufferType.GetMethod("DurationSeconds", [typeof(double)]);
Require(appendCapture is not null, "Missing PcmCaptureBuffer.Append");
Require(resetCapture is not null, "Missing PcmCaptureBuffer.Reset");
Require(snapshotCapture is not null, "Missing PcmCaptureBuffer.Snapshot");
Require(durationSeconds is not null, "Missing PcmCaptureBuffer.DurationSeconds");

appendCapture!.Invoke(captureBuffer, [new short[] { 0, 0, 0, 0 }]);
RequireProperty(captureBuffer!, "SampleCount", 4L);
RequireProperty(captureBuffer!, "HasAudibleSignal", false);
resetCapture!.Invoke(captureBuffer, null);
appendCapture.Invoke(captureBuffer, [new short[] { 0, 16_384, -16_384, 0 }]);
RequireProperty(captureBuffer!, "SampleCount", 4L);
RequireProperty(captureBuffer!, "HasAudibleSignal", true);
RequireNear(
    captureBufferType.GetProperty("PeakDbFs")?.GetValue(captureBuffer),
    -6.02,
    tolerance: 0.02,
    "PCM peak level must be reported in dBFS");
RequireNear(
    durationSeconds!.Invoke(captureBuffer, [16_000d]),
    0.00025,
    tolerance: 0.000001,
    "PCM duration must use the selected sample rate");

const string waveFileTypeName = "SayAll.Core.Audio.PcmWaveFile";
var waveFileType = coreAssembly.GetType(waveFileTypeName);
Require(waveFileType is not null, $"Missing {waveFileTypeName}");
var waveBytes = InvokeStatic(
    waveFileType!,
    "BuildMono16",
    snapshotCapture!.Invoke(captureBuffer, null)!,
    16_000) as byte[];
Require(waveBytes is not null, "PCM capture must build a WAV file");
Require(
    System.Text.Encoding.ASCII.GetString(waveBytes!, 0, 4) == "RIFF" &&
    System.Text.Encoding.ASCII.GetString(waveBytes!, 8, 4) == "WAVE" &&
    System.Text.Encoding.ASCII.GetString(waveBytes!, 36, 4) == "data" &&
    BitConverter.ToInt32(waveBytes!, 40) == 8,
    "PCM WAV header must describe four mono 16-bit samples");

Console.WriteLine("PASS PCM capture evidence and WAV playback artifact");

var spectrumAnalyzerType = coreAssembly.GetType(
    "SayAll.Core.Audio.VoiceSpectrumAnalyzer");
Require(spectrumAnalyzerType is not null,
    "Missing real-time VoiceSpectrumAnalyzer");
var analyzeSpectrum = spectrumAnalyzerType!.GetMethod(
    "Analyze",
    BindingFlags.Public | BindingFlags.Static,
    binder: null,
    types: [typeof(short[])],
    modifiers: null);
Require(analyzeSpectrum is not null,
    "VoiceSpectrumAnalyzer must analyze one real PCM frame");

var silentSpectrum = analyzeSpectrum!.Invoke(null, [new short[256]]);
var silentIntensities = (byte[]?)silentSpectrum?.GetType()
    .GetProperty("Intensities")?.GetValue(silentSpectrum);
var silentPeak = (double?)silentSpectrum?.GetType()
    .GetProperty("PeakDbFs")?.GetValue(silentSpectrum);
Require(silentIntensities is { Length: 24 } && silentIntensities.Max() <= 2 &&
    silentPeak <= -90,
    "Silence must render as a nearly black 24-band spectrum");

var toneSamples = Enumerable.Range(0, 256)
    .Select(index => (short)Math.Round(
        Math.Sin(2 * Math.PI * 440 * index / 16_000d) * 16_000))
    .ToArray();
var toneSpectrum = analyzeSpectrum.Invoke(null, [toneSamples]);
var toneIntensities = (byte[]?)toneSpectrum?.GetType()
    .GetProperty("Intensities")?.GetValue(toneSpectrum);
var dominantFrequency = (double?)toneSpectrum?.GetType()
    .GetProperty("DominantFrequencyHz")?.GetValue(toneSpectrum);
Require(toneIntensities is { Length: 24 } && toneIntensities.Max() >= 160 &&
    dominantFrequency is >= 340 and <= 540,
    "A 440 Hz PCM frame must immediately create a strong, correctly located spectral slice");

Console.WriteLine("PASS real PCM produces an immediate 24-band live spectrum slice");

const string accumulatorTypeName = "SayAll.Core.Audio.FrameAccumulator";
var accumulatorType = coreAssembly.GetType(accumulatorTypeName);
Require(accumulatorType is not null, $"Missing {accumulatorTypeName}");
var accumulator = Activator.CreateInstance(accumulatorType!);
Require(accumulator is not null, "Could not create ATVV frame accumulator");
var append = accumulatorType!.GetMethod("Append", [typeof(byte[]), typeof(int)]);
Require(append is not null, "Missing FrameAccumulator.Append");
RequireFrames(append!.Invoke(accumulator, [new byte[] { 1, 2 }, 3]));
RequireBytes(accumulatorType.GetProperty("Pending")?.GetValue(accumulator), 1, 2);
RequireFrames(
    append.Invoke(accumulator, [new byte[] { 3, 4, 5, 6, 7 }, 3]),
    new byte[] { 1, 2, 3 },
    new byte[] { 4, 5, 6 });
RequireBytes(accumulatorType.GetProperty("Pending")?.GetValue(accumulator), 7);

Console.WriteLine("PASS ATVV partial-frame accumulation");

var codexPreset = SayAll.Core.Input.RemoteBindingPresets.CreateCodex();
Require(
    codexPreset.Resolve(
        SayAll.Core.Input.RemoteButton.Up,
        SayAll.Core.Input.RemoteButtonTrigger.SingleClick).Action ==
    SayAll.Core.Input.RemoteButtonAction.PreviousChat &&
    codexPreset.Resolve(
        SayAll.Core.Input.RemoteButton.Down,
        SayAll.Core.Input.RemoteButtonTrigger.SingleClick).Action ==
    SayAll.Core.Input.RemoteButtonAction.NextChat,
    "Codex preset must scroll the current project conversation with Up and Down");
Require(
    codexPreset.Resolve(
        SayAll.Core.Input.RemoteButton.Ok,
        SayAll.Core.Input.RemoteButtonTrigger.SingleClick).Action ==
    SayAll.Core.Input.RemoteButtonAction.SubmitTask,
    "Codex preset must submit the current task with the RC003MS OK button");
Require(
    codexPreset.Resolve(
        SayAll.Core.Input.RemoteButton.Menu,
        SayAll.Core.Input.RemoteButtonTrigger.SingleClick).Action ==
    SayAll.Core.Input.RemoteButtonAction.OpenCommandMenu,
    "Codex preset must open the Codex activity view from the RC003MS Menu button");
Require(
    codexPreset.Resolve(
        SayAll.Core.Input.RemoteButton.Left,
        SayAll.Core.Input.RemoteButtonTrigger.SingleClick).Action ==
    SayAll.Core.Input.RemoteButtonAction.PreviousProject &&
    codexPreset.Resolve(
        SayAll.Core.Input.RemoteButton.Right,
        SayAll.Core.Input.RemoteButtonTrigger.SingleClick).Action ==
    SayAll.Core.Input.RemoteButtonAction.NextProject,
    "Codex preset must switch running projects with the RC003MS Left and Right buttons");
Require(
    codexPreset.Resolve(
        SayAll.Core.Input.RemoteButton.VolumeDown,
        SayAll.Core.Input.RemoteButtonTrigger.SingleClick).Action ==
    SayAll.Core.Input.RemoteButtonAction.SelectReasoning &&
    codexPreset.Resolve(
        SayAll.Core.Input.RemoteButton.VolumeUp,
        SayAll.Core.Input.RemoteButtonTrigger.SingleClick).Action ==
    SayAll.Core.Input.RemoteButtonAction.SelectModel,
    "Codex preset must open model and reasoning selectors with Volume Up and Down");
Require(
    codexPreset.Resolve(
        SayAll.Core.Input.RemoteButton.Back,
        SayAll.Core.Input.RemoteButtonTrigger.SingleClick).Action ==
    SayAll.Core.Input.RemoteButtonAction.GoBack,
    "Codex preset must preserve native Back behavior on the RC003MS Back button");
Require(
    codexPreset.Resolve(
        SayAll.Core.Input.RemoteButton.Home,
        SayAll.Core.Input.RemoteButtonTrigger.SingleClick).Action ==
    SayAll.Core.Input.RemoteButtonAction.NewChat,
    "Codex preset must start a new chat from the RC003MS Home button");
Require(
    codexPreset.Resolve(
        SayAll.Core.Input.RemoteButton.Power,
        SayAll.Core.Input.RemoteButtonTrigger.SingleClick).Action ==
    SayAll.Core.Input.RemoteButtonAction.ToggleSidebar,
    "Codex preset must let the RC003MS Power button recover or hide the chat sidebar");
Require(
    codexPreset.Resolve(
        SayAll.Core.Input.RemoteButton.Tv,
        SayAll.Core.Input.RemoteButtonTrigger.SingleClick).Action ==
    SayAll.Core.Input.RemoteButtonAction.OpenProjectTools &&
    codexPreset.Resolve(
        SayAll.Core.Input.RemoteButton.Tv,
        SayAll.Core.Input.RemoteButtonTrigger.DoubleClick).Action ==
    SayAll.Core.Input.RemoteButtonAction.InsertSkill,
    "Codex preset must open project tools on TV while preserving skill insertion on TV double-click");
Require(
    SayAll.Core.Input.RemoteButtonCatalog.StandardButtons.All(button =>
        codexPreset.Resolve(button, SayAll.Core.Input.RemoteButtonTrigger.SingleClick).Action !=
        SayAll.Core.Input.RemoteButtonAction.Disabled),
    "The Codex preset must give every physical RC003MS button a visible default action");

var customizedPreset = codexPreset.WithBinding(
    SayAll.Core.Input.RemoteButton.VolumeUp,
    SayAll.Core.Input.RemoteButtonTrigger.DoubleClick,
    new SayAll.Core.Input.RemoteButtonBinding(
        SayAll.Core.Input.RemoteButtonAction.SelectModel,
        "gpt-5.6-sol"));
Require(
    customizedPreset.Resolve(
        SayAll.Core.Input.RemoteButton.VolumeUp,
        SayAll.Core.Input.RemoteButtonTrigger.DoubleClick).Argument == "gpt-5.6-sol",
    "A clicked RC003MS button must accept a persisted custom action argument");

Console.WriteLine("PASS RC003MS Codex preset and per-button customization");

var bindingEditor = new SayAll.Core.Input.RemoteBindingEditorSession(codexPreset);
bindingEditor.SelectButton(SayAll.Core.Input.RemoteButton.Up);
bindingEditor.SelectTrigger(SayAll.Core.Input.RemoteButtonTrigger.DoubleClick);
bindingEditor.Assign(
    new SayAll.Core.Input.RemoteButtonBinding(
        SayAll.Core.Input.RemoteButtonAction.Find));
Require(
    bindingEditor.CurrentBinding.Action == SayAll.Core.Input.RemoteButtonAction.Find,
    "The editor must expose the action for the selected physical key and gesture");
Require(
    bindingEditor.Profile.Resolve(
        SayAll.Core.Input.RemoteButton.Up,
        SayAll.Core.Input.RemoteButtonTrigger.SingleClick).Action ==
    SayAll.Core.Input.RemoteButtonAction.PreviousChat,
    "Editing Up double-click must not overwrite Up single-click");
bindingEditor.SelectButton(SayAll.Core.Input.RemoteButton.Down);
Require(
    bindingEditor.CurrentBinding.Action == SayAll.Core.Input.RemoteButtonAction.Disabled,
    "Changing the selected physical key must show that key's independent gesture binding");
bindingEditor.ApplyProfile(SayAll.Core.Input.RemoteBindingPresets.CreateClaudeCode());
Require(
    bindingEditor.Profile.Name == "Claude Code" &&
    bindingEditor.SelectedButton == SayAll.Core.Input.RemoteButton.Down,
    "A starter template may replace mappings without losing the selected physical key");

Console.WriteLine("PASS RC003MS layout-first binding editor isolates every key and gesture");

var voiceToggle = new SayAll.Core.Audio.VoiceToggleController();
Require(
    voiceToggle.OnMicrophoneButtonPressed(hasSelectedInputMethod: false) ==
    SayAll.Core.Audio.VoiceToggleAction.RequestInputMethodSelection,
    "The first microphone press must request an input method when none is selected");
Require(
    voiceToggle.OnMicrophoneButtonPressed(hasSelectedInputMethod: true) ==
    SayAll.Core.Audio.VoiceToggleAction.OpenMicrophone,
    "A microphone press with a selected input method must start recording");
Require(
    voiceToggle.State == SayAll.Core.Audio.VoiceToggleState.Starting,
    "The toggle controller must wait for the real remote stream to start");
voiceToggle.NotifyVoiceStarted();
Require(
    voiceToggle.State == SayAll.Core.Audio.VoiceToggleState.Recording,
    "The controller must remain recording while the physical microphone key is held");
voiceToggle.NotifyVoiceStopped();
Require(
    voiceToggle.State == SayAll.Core.Audio.VoiceToggleState.Idle,
    "Releasing the physical microphone key must return the controller to idle");

Console.WriteLine("PASS RC003MS microphone hold-to-talk contract");

var microphoneGate = new SayAll.Core.Input.RemoteMicrophonePressGate(
    TimeSpan.FromMilliseconds(250));
var microphonePressedAt = new DateTimeOffset(
    2026,
    8,
    28,
    18,
    45,
    2,
    TimeSpan.FromHours(8));
Require(
    microphoneGate.TryAccept(microphonePressedAt) &&
    !microphoneGate.TryAccept(microphonePressedAt.AddMilliseconds(80)) &&
    microphoneGate.TryAccept(microphonePressedAt.AddSeconds(2)),
    "The RC003MS microphone HID report and matching ATVV notification must toggle only once per physical press");

Console.WriteLine("PASS RC003MS microphone HID and ATVV notifications are deduplicated");

var gestures = new SayAll.Core.Input.RemoteButtonGestureRecognizer(
    doubleClickWindow: TimeSpan.FromMilliseconds(300),
    longPressThreshold: TimeSpan.FromMilliseconds(600));
var gestureOrigin = new DateTimeOffset(2026, 8, 26, 19, 0, 0, TimeSpan.FromHours(8));
Require(
    gestures.Process([SayAll.Core.Input.RemoteButton.Down], gestureOrigin).Count == 0 &&
    gestures.Process([], gestureOrigin.AddMilliseconds(90)).Count == 0,
    "A short HID press must wait briefly so it can still become a double click");
var singleGestures = gestures.Flush(gestureOrigin.AddMilliseconds(400));
Require(
    singleGestures.Count == 1 &&
    singleGestures[0].Button == SayAll.Core.Input.RemoteButton.Down &&
    singleGestures[0].Trigger == SayAll.Core.Input.RemoteButtonTrigger.SingleClick,
    "One physical press/release must emit exactly one single-click action");

var doubleOrigin = gestureOrigin.AddSeconds(1);
gestures.Process([SayAll.Core.Input.RemoteButton.Ok], doubleOrigin);
gestures.Process([], doubleOrigin.AddMilliseconds(60));
gestures.Process([SayAll.Core.Input.RemoteButton.Ok], doubleOrigin.AddMilliseconds(170));
var doubleGestures = gestures.Process([], doubleOrigin.AddMilliseconds(230));
Require(
    doubleGestures.Count == 1 &&
    doubleGestures[0].Trigger == SayAll.Core.Input.RemoteButtonTrigger.DoubleClick,
    "Two nearby physical presses must emit one double-click action and no single click");

var longOrigin = gestureOrigin.AddSeconds(2);
gestures.Process([SayAll.Core.Input.RemoteButton.Menu], longOrigin);
var longGestures = gestures.Process([], longOrigin.AddMilliseconds(800));
Require(
    longGestures.Count == 1 &&
    longGestures[0].Trigger == SayAll.Core.Input.RemoteButtonTrigger.LongPress,
    "A held RC003MS key must emit one long-press action");

Console.WriteLine("PASS RC003MS single/double/long gesture recognition");

var responsiveGestures = new SayAll.Core.Input.RemoteButtonGestureRecognizer(
    doubleClickWindow: TimeSpan.FromMilliseconds(300),
    longPressThreshold: TimeSpan.FromMilliseconds(600),
    doubleClickButtons: [SayAll.Core.Input.RemoteButton.Tv]);
var responsiveOrigin = gestureOrigin.AddSeconds(3);
responsiveGestures.Process(
    [SayAll.Core.Input.RemoteButton.VolumeUp],
    responsiveOrigin);
var firstResponsiveClick = responsiveGestures.Process(
    [],
    responsiveOrigin.AddMilliseconds(80));
responsiveGestures.Process(
    [SayAll.Core.Input.RemoteButton.VolumeUp],
    responsiveOrigin.AddMilliseconds(170));
var secondResponsiveClick = responsiveGestures.Process(
    [],
    responsiveOrigin.AddMilliseconds(240));
Require(
    firstResponsiveClick.Count == 1 &&
    firstResponsiveClick[0].Trigger == SayAll.Core.Input.RemoteButtonTrigger.SingleClick &&
    secondResponsiveClick.Count == 1 &&
    secondResponsiveClick[0].Trigger == SayAll.Core.Input.RemoteButtonTrigger.SingleClick,
    "Keys without a configured double-click must dispatch every release immediately instead of swallowing rapid presses");

var tvOrigin = responsiveOrigin.AddSeconds(1);
responsiveGestures.Process([SayAll.Core.Input.RemoteButton.Tv], tvOrigin);
Require(
    responsiveGestures.Process([], tvOrigin.AddMilliseconds(60)).Count == 0,
    "The one key with a configured double-click must still wait for the second click");
responsiveGestures.Process(
    [SayAll.Core.Input.RemoteButton.Tv],
    tvOrigin.AddMilliseconds(160));
var tvDoubleClick = responsiveGestures.Process(
    [],
    tvOrigin.AddMilliseconds(220));
Require(
    tvDoubleClick.Count == 1 &&
    tvDoubleClick[0].Button == SayAll.Core.Input.RemoteButton.Tv &&
    tvDoubleClick[0].Trigger == SayAll.Core.Input.RemoteButtonTrigger.DoubleClick,
    "TV must preserve its configured double-click gesture");

Console.WriteLine("PASS only configured double-click keys delay RC003MS single-click dispatch");

static bool InvokeCanContinue(
    MethodInfo method,
    object step,
    Type capabilitiesType,
    params (string Name, object Value)[] values)
{
    var capabilities = Activator.CreateInstance(capabilitiesType)
        ?? throw new InvalidOperationException("Could not create onboarding capabilities");
    foreach (var (name, value) in values)
    {
        var property = capabilitiesType.GetProperty(name);
        Require(property is not null, $"Missing OnboardingCapabilities.{name}");
        property!.SetValue(capabilities, value);
    }
    return method.Invoke(null, [step, capabilities]) as bool? ?? false;
}

static object RequiredEnumValue(Type enumType, string name)
{
    Require(Enum.TryParse(enumType, name, out var value), $"Missing {enumType.FullName}.{name}");
    return value!;
}

static void RequireUsageSet(object? value, params ushort[] expected)
{
    Require(value is IEnumerable<ushort>, "HID parser must return a usage set");
    var actual = ((IEnumerable<ushort>)value!).Order().ToArray();
    var wanted = expected.Order().ToArray();
    Require(
        actual.SequenceEqual(wanted),
        $"Expected usages [{string.Join(",", wanted.Select(item => $"0x{item:X4}"))}], " +
        $"got [{string.Join(",", actual.Select(item => $"0x{item:X4}"))}]");
}

static void RequireProperty<T>(object target, string name, T expected)
{
    var property = target.GetType().GetProperty(name);
    Require(property is not null, $"Missing {target.GetType().Name}.{name}");
    var actual = property!.GetValue(target);
    Require(
        Equals(actual, expected),
        $"Expected {target.GetType().Name}.{name}={expected}, got {actual}");
}

static object? InvokeStatic(Type type, string methodName, params object[] arguments)
{
    var method = type.GetMethods(BindingFlags.Public | BindingFlags.Static)
        .SingleOrDefault(candidate =>
            candidate.Name == methodName && candidate.GetParameters().Length == arguments.Length);
    Require(method is not null, $"Missing {type.Name}.{methodName}");
    return method!.Invoke(null, arguments);
}

static void RequireBytes(object? value, params byte[] expected)
{
    Require(value is byte[], "Expected a byte array");
    var actual = (byte[])value!;
    Require(
        actual.SequenceEqual(expected),
        $"Expected bytes {Convert.ToHexString(expected)}, got {Convert.ToHexString(actual)}");
}

static void RequireShorts(object? value, params short[] expected)
{
    Require(value is short[], "Expected a 16-bit PCM sample array");
    var actual = (short[])value!;
    Require(
        actual.SequenceEqual(expected),
        $"Expected samples [{string.Join(",", expected)}], got [{string.Join(",", actual)}]");
}

static void RequireFrames(object? value, params byte[][] expected)
{
    Require(value is IEnumerable<byte[]>, "Expected an ATVV frame collection");
    var actual = ((IEnumerable<byte[]>)value!).ToArray();
    Require(actual.Length == expected.Length, $"Expected {expected.Length} frames, got {actual.Length}");
    for (var index = 0; index < expected.Length; index++)
    {
        Require(
            actual[index].SequenceEqual(expected[index]),
            $"ATVV frame {index} differs: expected {Convert.ToHexString(expected[index])}, " +
            $"got {Convert.ToHexString(actual[index])}");
    }
}

static void RequireFramesAsPcm(object? value, params short[][] expected)
{
    Require(value is IEnumerable<short[]>, "Expected a PCM frame collection");
    var actual = ((IEnumerable<short[]>)value!).ToArray();
    Require(actual.Length == expected.Length, $"Expected {expected.Length} PCM frames, got {actual.Length}");
    for (var index = 0; index < expected.Length; index++)
    {
        Require(
            actual[index].SequenceEqual(expected[index]),
            $"PCM frame {index} differs");
    }
}

static void RequireSinglePcmFrame(object? value, int expectedLength, short expectedFirstSample)
{
    Require(value is IEnumerable<short[]>, "Expected a PCM frame collection");
    var actual = ((IEnumerable<short[]>)value!).ToArray();
    Require(actual.Length == 1, $"Expected one PCM frame, got {actual.Length}");
    Require(actual[0].Length == expectedLength, $"Expected {expectedLength} PCM samples, got {actual[0].Length}");
    Require(actual[0][0] == expectedFirstSample, $"Expected first PCM sample {expectedFirstSample}, got {actual[0][0]}");
}

static void RequireNear(object? value, double expected, double tolerance, string message)
{
    Require(value is double, message);
    Require(Math.Abs((double)value! - expected) <= tolerance, message);
}

static void Require(bool condition, string message)
{
    if (!condition)
    {
        Console.Error.WriteLine($"FAIL {message}");
        Environment.Exit(1);
    }
}
