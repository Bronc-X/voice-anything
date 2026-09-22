using Windows.Devices.Enumeration;
using Windows.Media;
using Windows.Media.Audio;
using Windows.Media.Devices;
using Windows.Media.MediaProperties;

namespace SayAll.Windows;

public sealed class PcmAudioOutput : IDisposable
{
    private AudioGraph? graph;
    private AudioFrameInputNode? inputNode;
    private AudioDeviceOutputNode? outputNode;

    public string? DeviceName { get; private set; }

    public string? CaptureDeviceId { get; private set; }

    public async Task InitializeCableAsync()
    {
        Dispose();
        var devices = await DeviceInformation.FindAllAsync(MediaDevice.GetAudioRenderSelector());
        var cable = devices.FirstOrDefault(device =>
            device.Name.Equals(
                "CABLE Input (VB-Audio Virtual Cable)",
                StringComparison.OrdinalIgnoreCase));
        if (cable is null)
        {
            throw new InvalidOperationException(
                "未找到 CABLE Input (VB-Audio Virtual Cable)，请确认 VB-CABLE 已启用");
        }

        var captureDevices = await DeviceInformation.FindAllAsync(
            MediaDevice.GetAudioCaptureSelector());
        var cableOutput = captureDevices.FirstOrDefault(device =>
            device.Name.StartsWith("CABLE Output", StringComparison.OrdinalIgnoreCase));
        if (cableOutput is null)
        {
            throw new InvalidOperationException(
                "未找到 CABLE Output 录音端点，请确认 VB-CABLE 已启用");
        }

        var settings = new AudioGraphSettings(global::Windows.Media.Render.AudioRenderCategory.Media)
        {
            EncodingProperties = AudioEncodingProperties.CreatePcm(16_000, 1, 16),
            QuantumSizeSelectionMode = QuantumSizeSelectionMode.ClosestToDesired,
            DesiredSamplesPerQuantum = 240,
            PrimaryRenderDevice = cable,
        };
        var graphResult = await AudioGraph.CreateAsync(settings);
        if (graphResult.Status != AudioGraphCreationStatus.Success || graphResult.Graph is null)
        {
            throw new InvalidOperationException($"Windows 音频图创建失败（{graphResult.Status}）");
        }

        graph = graphResult.Graph;
        var outputResult = await graph.CreateDeviceOutputNodeAsync();
        if (outputResult.Status != AudioDeviceNodeCreationStatus.Success ||
            outputResult.DeviceOutputNode is null)
        {
            Dispose();
            throw new InvalidOperationException($"VB-CABLE 输出节点创建失败（{outputResult.Status}）");
        }

        outputNode = outputResult.DeviceOutputNode;
        inputNode = graph.CreateFrameInputNode(
            AudioEncodingProperties.CreatePcm(16_000, 1, 16));
        inputNode.AddOutgoingConnection(outputNode);
        inputNode.Start();
        graph.Start();
        DeviceName = cable.Name;
        CaptureDeviceId = cableOutput.Id;
    }

    public unsafe void Write(short[] samples)
    {
        ArgumentNullException.ThrowIfNull(samples);
        var target = inputNode ?? throw new InvalidOperationException("音频输出尚未初始化");
        using var frame = new AudioFrame((uint)(samples.Length * sizeof(short)));
        PcmAudioFrameWriter.Write(frame, samples);
        target.AddFrame(frame);
    }

    public void Dispose()
    {
        inputNode?.Stop();
        graph?.Stop();
        inputNode?.Dispose();
        outputNode?.Dispose();
        graph?.Dispose();
        inputNode = null;
        outputNode = null;
        graph = null;
        DeviceName = null;
        CaptureDeviceId = null;
    }
}
