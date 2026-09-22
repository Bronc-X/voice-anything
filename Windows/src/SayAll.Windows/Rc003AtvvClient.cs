using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Enumeration;
using Windows.Storage.Streams;
using SayAll.Core.Audio;
using SayAll.HidBridge.Contracts;

namespace SayAll.Windows;

public sealed class Rc003AtvvClient : IAsyncDisposable
{
    private static readonly TimeSpan MicrophoneKeepAliveInterval = TimeSpan.FromSeconds(10);
    private readonly object microphoneSessionGate = new();
    private GattDeviceService? service;
    private BluetoothLEDevice? selectedDevice;
    private GattCharacteristic? transmit;
    private GattCharacteristic? audio;
    private GattCharacteristic? control;
    private AtvvCapabilities? capabilities;
    private AtvvVoiceDecoder? voiceDecoder;
    private CancellationTokenSource? microphoneSessionCts;
    private byte sessionId;
    private volatile bool voiceStarted;

    public event Action<string>? StatusChanged;
    public event Action<AtvvCapabilities>? CapabilitiesReceived;
    public event Action? RemoteMicrophonePressed;
    public event Action? VoiceStarted;
    public event Action<short[]>? PcmFrameReady;
    public event Action? VoiceStopped;
    public event Action<Exception>? Failed;

    public bool IsConnected { get; private set; }

    public async Task ConnectAsync(CancellationToken cancellationToken = default, SelectedRemote? selected = null)
    {
        await DisconnectAsync();
        try
        {
        StatusChanged?.Invoke("正在查找 RC003MS 语音服务…");

        var selection = selected ?? DeviceSelection.Read() ??
            throw new InvalidOperationException("请先选择并验证一只遥控器。");
        selectedDevice = await BluetoothLEDevice.FromBluetoothAddressAsync(
            Convert.ToUInt64(selection.BluetoothAddress, 16)).AsTask(cancellationToken);
        if (selectedDevice is null || !selectedDevice.DeviceInformation.Pairing.IsPaired)
            throw new InvalidOperationException("所选遥控器未配对或不可用。");
        var result = await selectedDevice.GetGattServicesForUuidAsync(
            AtvvProtocol.ServiceUuid, BluetoothCacheMode.Uncached).AsTask(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (result.Status != GattCommunicationStatus.Success || result.Services.Count != 1)
        {
            foreach (var candidate in result.Services) candidate.Dispose();
            throw new InvalidOperationException("所选遥控器没有唯一可用的 ATVV 语音服务。");
        }
        service = result.Services[0];

        if (service is null)
        {
            throw new UnauthorizedAccessException("Windows 拒绝打开 RC003MS 的 ATVV 语音服务");
        }

        transmit = await GetCharacteristicAsync(service, AtvvProtocol.TransmitUuid, cancellationToken);
        audio = await GetCharacteristicAsync(service, AtvvProtocol.AudioUuid, cancellationToken);
        control = await GetCharacteristicAsync(service, AtvvProtocol.ControlUuid, cancellationToken);

        audio.ValueChanged += Audio_ValueChanged;
        control.ValueChanged += Control_ValueChanged;

        await EnableNotificationsAsync(audio, "音频", cancellationToken);
        await EnableNotificationsAsync(control, "控制", cancellationToken);
        await WriteAsync(AtvvProtocol.GetCapabilitiesV10(), cancellationToken);

        IsConnected = true;
        StatusChanged?.Invoke("RC003MS 语音服务已连接，正在读取能力…");
        }
        catch { await DisconnectAsync(); throw; }
    }

    public async Task DisconnectAsync()
    {
        StopMicrophoneSession();
        IsConnected = false;
        sessionId = 0;
        voiceStarted = false;
        if (audio is not null)
        {
            audio.ValueChanged -= Audio_ValueChanged;
        }

        if (control is not null)
        {
            control.ValueChanged -= Control_ValueChanged;
        }

        audio = null;
        control = null;
        transmit = null;
        capabilities = null;
        voiceDecoder = null;
        service?.Dispose();
        service = null;
        selectedDevice?.Dispose();
        selectedDevice = null;
        await Task.CompletedTask;
    }

    public async Task RequestMicrophoneOpenAsync(
        CancellationToken cancellationToken = default)
    {
        var currentCapabilities = capabilities
            ?? throw new InvalidOperationException("RC003MS 语音能力尚未就绪");
        var sessionToken = StartMicrophoneSession();
        try
        {
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                sessionToken);
            await WriteAsync(
                AtvvProtocol.MicrophoneOpen(
                    currentCapabilities.Version,
                    currentCapabilities.SelectedCodec),
                linkedCts.Token);
            _ = RunMicrophoneKeepAliveAsync(sessionToken);
        }
        catch
        {
            StopMicrophoneSession();
            throw;
        }
    }

    public async Task RequestMicrophoneCloseAsync(
        CancellationToken cancellationToken = default)
    {
        var currentCapabilities = capabilities
            ?? throw new InvalidOperationException("RC003MS 语音能力尚未就绪");
        StopMicrophoneSession();
        await WriteAsync(
            AtvvProtocol.MicrophoneClose(currentCapabilities.Version, sessionId),
            cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync();
    }

    private static async Task<GattCharacteristic> GetCharacteristicAsync(
        GattDeviceService targetService,
        Guid uuid,
        CancellationToken cancellationToken)
    {
        var result = await targetService.GetCharacteristicsForUuidAsync(
            uuid,
            BluetoothCacheMode.Uncached).AsTask(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (result.Status != GattCommunicationStatus.Success || result.Characteristics.Count == 0)
        {
            throw new InvalidOperationException(
                $"RC003MS 特征 {uuid} 不可用（{result.Status}）");
        }

        return result.Characteristics[0];
    }

    private static async Task EnableNotificationsAsync(
        GattCharacteristic characteristic,
        string label,
        CancellationToken cancellationToken)
    {
        var status = await characteristic.WriteClientCharacteristicConfigurationDescriptorAsync(
            GattClientCharacteristicConfigurationDescriptorValue.Notify).AsTask(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (status != GattCommunicationStatus.Success)
        {
            throw new InvalidOperationException($"无法订阅 RC003MS {label}通知（{status}）");
        }
    }

    private async Task WriteAsync(byte[] payload, CancellationToken cancellationToken = default)
    {
        var target = transmit ?? throw new InvalidOperationException("ATVV 发送特征尚未连接");
        using var writer = new DataWriter();
        writer.WriteBytes(payload);
        var result = await target.WriteValueWithResultAsync(
            writer.DetachBuffer(),
            GattWriteOption.WriteWithoutResponse).AsTask(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (result.Status != GattCommunicationStatus.Success)
        {
            throw new InvalidOperationException($"RC003MS ATVV 写入失败（{result.Status}）");
        }
    }

    private void Audio_ValueChanged(
        GattCharacteristic sender,
        GattValueChangedEventArgs args)
    {
        try
        {
            var payload = ReadBytes(args.CharacteristicValue);
            if (voiceDecoder is null)
            {
                return;
            }

            if (!voiceDecoder.IsStreaming)
            {
                voiceDecoder.ApplyControl(new AtvvControlPacket(AtvvControlKind.StreamStarted));
                NotifyVoiceStarted();
            }

            foreach (var pcmFrame in voiceDecoder.AppendAudio(payload))
            {
                PcmFrameReady?.Invoke(pcmFrame);
            }
        }
        catch (Exception exception)
        {
            Failed?.Invoke(exception);
        }
    }

    private void Control_ValueChanged(
        GattCharacteristic sender,
        GattValueChangedEventArgs args)
    {
        HandleControl(ReadBytes(args.CharacteristicValue));
    }

    private void HandleControl(byte[] payload)
    {
        try
        {
            var parsedCapabilities = AtvvCapabilities.Parse(payload);
            if (parsedCapabilities is not null)
            {
                if (parsedCapabilities.SampleRate != 16_000 || (parsedCapabilities.Codecs & 2) == 0 ||
                    parsedCapabilities.FrameSize is < 1 or > 4096 || parsedCapabilities.Version > 0x0100)
                    throw new InvalidOperationException("当前适配器只支持 ATVV v0/v1 的 16 kHz ADPCM，请为该音频格式添加适配器。");
                capabilities = parsedCapabilities;
                voiceDecoder = new AtvvVoiceDecoder(parsedCapabilities.FrameSize);
                CapabilitiesReceived?.Invoke(parsedCapabilities);
                StatusChanged?.Invoke(
                    $"RC003MS 语音就绪：{parsedCapabilities.SampleRate / 1000:0} kHz");
                return;
            }

            var packet = AtvvControlPacket.Parse(payload);
            if (packet is null)
            {
                return;
            }

            switch (packet.Kind)
            {
                case AtvvControlKind.RemoteMicrophoneRequested:
                    RemoteMicrophonePressed?.Invoke();
                    break;
                case AtvvControlKind.StreamStarted:
                    voiceDecoder?.ApplyControl(packet);
                    sessionId = packet.SessionId;
                    NotifyVoiceStarted();
                    break;
                case AtvvControlKind.StreamStopped:
                    voiceDecoder?.ApplyControl(packet);
                    if (voiceStarted)
                    {
                        voiceStarted = false;
                        VoiceStopped?.Invoke();
                    }
                    StopMicrophoneSession();
                    break;
                case AtvvControlKind.DecoderSynchronized:
                    voiceDecoder?.ApplyControl(packet);
                    break;
            }
        }
        catch (Exception exception)
        {
            Failed?.Invoke(exception);
        }
    }

    private void NotifyVoiceStarted()
    {
        if (voiceStarted)
        {
            return;
        }

        voiceStarted = true;
        VoiceStarted?.Invoke();
    }

    private CancellationToken StartMicrophoneSession()
    {
        CancellationTokenSource? previous;
        CancellationTokenSource current;
        lock (microphoneSessionGate)
        {
            previous = microphoneSessionCts;
            current = new CancellationTokenSource();
            microphoneSessionCts = current;
        }

        previous?.Cancel();
        previous?.Dispose();
        return current.Token;
    }

    private void StopMicrophoneSession()
    {
        CancellationTokenSource? current;
        lock (microphoneSessionGate)
        {
            current = microphoneSessionCts;
            microphoneSessionCts = null;
        }

        current?.Cancel();
        current?.Dispose();
    }

    private async Task RunMicrophoneKeepAliveAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (true)
            {
                await Task.Delay(MicrophoneKeepAliveInterval, cancellationToken);
                if (!voiceStarted || !IsMicrophoneSessionCurrent(cancellationToken))
                {
                    continue;
                }

                var currentCapabilities = capabilities
                    ?? throw new InvalidOperationException("RC003MS 语音能力尚未就绪");
                var payload = AtvvProtocol.MicrophoneExtend(
                    currentCapabilities.Version,
                    sessionId) ?? AtvvProtocol.MicrophoneOpen(
                    currentCapabilities.Version,
                    currentCapabilities.SelectedCodec);
                await WriteAsync(payload, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            Failed?.Invoke(exception);
        }
    }

    private bool IsMicrophoneSessionCurrent(CancellationToken cancellationToken)
    {
        lock (microphoneSessionGate)
        {
            return microphoneSessionCts is not null &&
                microphoneSessionCts.Token == cancellationToken;
        }
    }

    private static byte[] ReadBytes(IBuffer buffer)
    {
        using var reader = DataReader.FromBuffer(buffer);
        var payload = new byte[reader.UnconsumedBufferLength];
        reader.ReadBytes(payload);
        return payload;
    }
}
