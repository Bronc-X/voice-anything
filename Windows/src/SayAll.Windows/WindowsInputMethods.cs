using System.Globalization;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace SayAll.Windows;

public sealed record WindowsInputMethodProfile(
    string Id,
    ushort LanguageId,
    Guid TipClassId,
    Guid ProfileId,
    string DisplayName,
    string LanguageName)
{
    public static WindowsInputMethodProfile Parse(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        var separator = id.IndexOf(':');
        if (separator != 4 || id.Length != 4 + 1 + 38 + 38)
        {
            throw new FormatException("输入法标识格式无效。" );
        }

        if (!ushort.TryParse(
                id.AsSpan(0, 4),
                NumberStyles.HexNumber,
                CultureInfo.InvariantCulture,
                out var languageId) ||
            !Guid.TryParse(id.AsSpan(5, 38), out var tipClassId) ||
            !Guid.TryParse(id.AsSpan(43, 38), out var profileId))
        {
            throw new FormatException("输入法标识格式无效。" );
        }

        return new WindowsInputMethodProfile(
            id,
            languageId,
            tipClassId,
            profileId,
            id,
            GetLanguageName(languageId));
    }

    internal static string GetLanguageName(ushort languageId)
    {
        try
        {
            return CultureInfo.GetCultureInfo(languageId).NativeName;
        }
        catch (CultureNotFoundException)
        {
            return $"0x{languageId:X4}";
        }
    }
}

public static class WindowsInputMethodCatalog
{
    private const string SortOrderPath =
        @"Software\Microsoft\CTF\SortOrder\AssemblyItem";
    private const string TipRootPath = @"SOFTWARE\Microsoft\CTF\TIP";

    public static IReadOnlyList<WindowsInputMethodProfile> GetConfiguredProfiles()
    {
        var profiles = new List<WindowsInputMethodProfile>();
        using var sortOrderRoot = Registry.CurrentUser.OpenSubKey(SortOrderPath);
        if (sortOrderRoot is null)
        {
            return profiles;
        }

        foreach (var languageKeyName in sortOrderRoot.GetSubKeyNames())
        {
            if (!TryParseLanguageKey(languageKeyName, out var languageId))
            {
                continue;
            }

            using var languageKey = sortOrderRoot.OpenSubKey(languageKeyName);
            if (languageKey is null)
            {
                continue;
            }

            foreach (var categoryName in languageKey.GetSubKeyNames())
            {
                using var categoryKey = languageKey.OpenSubKey(categoryName);
                if (categoryKey is null)
                {
                    continue;
                }

                foreach (var orderName in categoryKey.GetSubKeyNames().Order())
                {
                    using var orderKey = categoryKey.OpenSubKey(orderName);
                    if (!TryReadGuid(orderKey, "CLSID", out var tipClassId) ||
                        !TryReadGuid(orderKey, "Profile", out var profileId))
                    {
                        continue;
                    }

                    var id = $"{languageId:X4}:{tipClassId:B}{profileId:B}";
                    if (profiles.Any(profile =>
                            profile.Id.Equals(id, StringComparison.OrdinalIgnoreCase)))
                    {
                        continue;
                    }

                    profiles.Add(new WindowsInputMethodProfile(
                        id,
                        languageId,
                        tipClassId,
                        profileId,
                        GetProfileDescription(languageId, tipClassId, profileId),
                        WindowsInputMethodProfile.GetLanguageName(languageId)));
                }
            }
        }

        return profiles;
    }

    private static bool TryParseLanguageKey(string name, out ushort languageId)
    {
        var text = name.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? name[2..]
            : name;
        return ushort.TryParse(
            text,
            NumberStyles.HexNumber,
            CultureInfo.InvariantCulture,
            out languageId);
    }

    private static bool TryReadGuid(
        RegistryKey? key,
        string valueName,
        out Guid value)
    {
        value = Guid.Empty;
        return key?.GetValue(valueName) is string text &&
            Guid.TryParse(text, out value) &&
            value != Guid.Empty;
    }

    private static string GetProfileDescription(
        ushort languageId,
        Guid tipClassId,
        Guid profileId)
    {
        var path = $@"{TipRootPath}\{tipClassId:B}\LanguageProfile\0x{languageId:X8}\{profileId:B}";
        using var profileKey = Registry.LocalMachine.OpenSubKey(path);
        return profileKey?.GetValue("Description") as string
            ?? $"输入法 {profileId:B}";
    }
}

public enum WindowsVoiceInputKind
{
    CodexDictation,
    Typeless,
    WeChatVoice,
    BageShuo,
}

public sealed record WindowsVoiceInputProfile(
    string Id,
    string DisplayName,
    WindowsVoiceInputKind Kind,
    WindowsInputMethodProfile? InputMethodProfile);

public static class WindowsVoiceInputCatalog
{
    public const string CodexDictationId = "app:codex-dictation";
    public const string TypelessId = "app:typeless";
    public const string WeChatVoiceId = "app:wechat-voice";
    public const string BageShuoId = "app:bageshuo";
    private const string LegacyWeTypeId =
        "0804:{86598FB9-66A2-463E-B9C2-AEB906D477AD}{607FDF85-FCC8-4DBD-A365-41296F980C9C}";

    public static IReadOnlyList<WindowsVoiceInputProfile> GetAvailableProfiles()
    {
        return
        [
            new WindowsVoiceInputProfile(
                TypelessId,
                "Typeless",
                WindowsVoiceInputKind.Typeless,
                null),
            new WindowsVoiceInputProfile(
                CodexDictationId,
                "Codex 语音听写",
                WindowsVoiceInputKind.CodexDictation,
                null),
            new WindowsVoiceInputProfile(
                WeChatVoiceId,
                "微信语音输入法",
                WindowsVoiceInputKind.WeChatVoice,
                null),
            new WindowsVoiceInputProfile(
                BageShuoId,
                "网易叭哥说",
                WindowsVoiceInputKind.BageShuo,
                null),
        ];
    }

    internal static string GetTypelessExecutablePath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Programs",
        "Typeless",
        "Typeless.exe");

    internal static string GetWeChatExecutablePath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
        "Tencent",
        "Weixin",
        "Weixin.exe");

    internal static string GetBageShuoExecutablePath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "bageshuo",
        "bageshuo.exe");

    public static string? NormalizePersistedId(string? id)
    {
        return id?.Equals(LegacyWeTypeId, StringComparison.OrdinalIgnoreCase) == true
            ? WeChatVoiceId
            : id;
    }
}

public sealed record WindowsVoiceInputShortcut(IReadOnlyList<ushort> Keys);

public static class WindowsVoiceInputShortcutCatalog
{
    public static WindowsVoiceInputShortcut Resolve(WindowsVoiceInputKind kind)
    {
        return kind switch
        {
            WindowsVoiceInputKind.CodexDictation =>
                new([(ushort)0x11, (ushort)0x10, (ushort)0x44]),
            WindowsVoiceInputKind.Typeless => new([(ushort)0xA4]),
            WindowsVoiceInputKind.WeChatVoice => new([(ushort)0xA2, (ushort)0x5B]),
            WindowsVoiceInputKind.BageShuo => new([(ushort)0xA5]),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
    }
}

public sealed record WindowsVoiceInputKeyTransition(ushort Key, bool KeyUp);

public static class WindowsVoiceInputInteractionPolicy
{
    public static bool IsTapToToggle(WindowsVoiceInputKind kind)
    {
        return kind is WindowsVoiceInputKind.Typeless or WindowsVoiceInputKind.BageShuo;
    }

    public static string GetHint(WindowsVoiceInputKind kind)
    {
        return IsTapToToggle(kind)
            ? "遥控器按住说话、松开结束；VoiceAnything 会在按下时轻触一次开始，松开时再轻触一次结束，请不要长按 Alt。"
            : "遥控器按住说话，松开结束。";
    }

    public static string GetRecordingHint(WindowsVoiceInputKind kind)
    {
        return IsTapToToggle(kind)
            ? $"已轻触 {GetShortcutLabel(kind)} 开始；请继续按住遥控器说话，松开后自动结束。"
            : "快捷键已按下；请继续按住遥控器说话。";
    }

    public static string GetStoppedHint(WindowsVoiceInputKind kind)
    {
        return IsTapToToggle(kind)
            ? $"已轻触 {GetShortcutLabel(kind)} 结束；本次录音已停止。"
            : "快捷键已松开；本次录音已停止。";
    }

    private static string GetShortcutLabel(WindowsVoiceInputKind kind)
    {
        return kind switch
        {
            WindowsVoiceInputKind.Typeless => "Left Alt",
            WindowsVoiceInputKind.BageShuo => "Right Alt",
            _ => "语音快捷键",
        };
    }
}

public static class WindowsVoiceInputActivationPlan
{
    public static IReadOnlyList<WindowsVoiceInputKeyTransition> Create(
        WindowsVoiceInputKind kind,
        bool starting)
    {
        var keys = WindowsVoiceInputShortcutCatalog.Resolve(kind).Keys;
        if (WindowsVoiceInputInteractionPolicy.IsTapToToggle(kind))
        {
            return keys.Select(key =>
                    new WindowsVoiceInputKeyTransition(key, KeyUp: false))
                .Concat(keys.Reverse().Select(key =>
                    new WindowsVoiceInputKeyTransition(key, KeyUp: true)))
                .ToArray();
        }

        return starting
            ? keys.Select(key => new WindowsVoiceInputKeyTransition(key, KeyUp: false)).ToArray()
            : keys.Reverse()
                .Select(key => new WindowsVoiceInputKeyTransition(key, KeyUp: true))
                .ToArray();
    }
}

public static class WindowsVoiceInputDispatchPolicy
{
    public static TimeSpan BageShuoStartupTimeout { get; } =
        TimeSpan.FromSeconds(3);
}

public static class VoiceRuntimeStartupPolicy
{
    public static bool ShouldConnect(
        bool bluetoothConnected,
        string? preferredInputMethodId,
        bool isConnected,
        bool isConnecting)
    {
        return bluetoothConnected &&
            !string.IsNullOrWhiteSpace(preferredInputMethodId) &&
            !isConnected &&
            !isConnecting;
    }
}

public static class WindowsVoiceInputActivator
{
    private const uint InputKeyboard = 1;
    private const uint KeyEventKeyUp = 0x0002;
    private static readonly object SyncRoot = new();
    private static WindowsVoiceInputKind? activeKind;

    public static void Start(WindowsVoiceInputProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (profile.Kind == WindowsVoiceInputKind.WeChatVoice)
        {
            EnsureWeChatIsRunning();
        }
        else if (profile.Kind == WindowsVoiceInputKind.BageShuo)
        {
            EnsureBageShuoIsRunning();
        }

        lock (SyncRoot)
        {
            if (activeKind is not null)
            {
                return;
            }

            SendActivation(profile.Kind, starting: true);
            activeKind = profile.Kind;
        }
    }

    public static void Stop()
    {
        lock (SyncRoot)
        {
            if (activeKind is not { } kind)
            {
                return;
            }

            SendActivation(kind, starting: false);
            activeKind = null;
        }
    }

    private static void SendActivation(WindowsVoiceInputKind kind, bool starting)
    {
        foreach (var transition in WindowsVoiceInputActivationPlan.Create(kind, starting))
        {
            Send([CreateKeyboardInput(transition.Key, transition.KeyUp)]);
        }
    }

    private static void EnsureWeChatIsRunning()
    {
        if (Process.GetProcessesByName("Weixin").Length > 0)
        {
            return;
        }

        var executablePath = WindowsVoiceInputCatalog.GetWeChatExecutablePath();
        if (!File.Exists(executablePath))
        {
            throw new InvalidOperationException("本机未安装支持全局语音输入的微信。" );
        }

        Process.Start(new ProcessStartInfo(executablePath)
        {
            UseShellExecute = true,
        });
    }

    private static void EnsureBageShuoIsRunning()
    {
        if (Process.GetProcessesByName("hotkey-listener").Length > 0)
        {
            return;
        }

        var executablePath = WindowsVoiceInputCatalog.GetBageShuoExecutablePath();
        if (!File.Exists(executablePath))
        {
            throw new InvalidOperationException("本机未安装网易叭哥说。" );
        }

        if (Process.GetProcessesByName("bageshuo").Length == 0)
        {
            Process.Start(new ProcessStartInfo(executablePath)
            {
                UseShellExecute = true,
            });
        }

        var startupTimer = Stopwatch.StartNew();
        while (startupTimer.Elapsed < WindowsVoiceInputDispatchPolicy.BageShuoStartupTimeout)
        {
            if (Process.GetProcessesByName("hotkey-listener").Length > 0)
            {
                return;
            }

            Thread.Sleep(50);
        }

        throw new InvalidOperationException(
            "网易叭哥说已启动，但快捷键服务尚未就绪。请先完成登录后重试。" );
    }

    private static KeyboardInputEnvelope CreateKeyboardInput(ushort key, bool keyUp)
    {
        return new KeyboardInputEnvelope
        {
            Type = InputKeyboard,
            Union = new KeyboardInputUnion
            {
                Keyboard = new KeyboardInput
                {
                    VirtualKey = key,
                    Flags = keyUp ? KeyEventKeyUp : 0,
                },
            },
        };
    }

    private static void Send(IReadOnlyCollection<KeyboardInputEnvelope> inputs)
    {
        var sent = SendInput(
            (uint)inputs.Count,
            inputs.ToArray(),
            Marshal.SizeOf<KeyboardInputEnvelope>());
        if (sent != inputs.Count)
        {
            throw new InvalidOperationException("Windows 未能启动所选语音输入。" );
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(
        uint inputCount,
        [In] KeyboardInputEnvelope[] inputs,
        int size);

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInputEnvelope
    {
        public uint Type;
        public KeyboardInputUnion Union;
    }

    [StructLayout(LayoutKind.Explicit, Size = 32)]
    private struct KeyboardInputUnion
    {
        [FieldOffset(0)]
        public KeyboardInput Keyboard;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInput
    {
        public ushort VirtualKey;
        public ushort ScanCode;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }
}

public static class WindowsInputMethodActivator
{
    private const uint TfProfileTypeInputProcessor = 1;
    private const uint TfIppmfDontCareCurrentInputLanguage = 0x00000004;
    private const uint TfIppmfForSession = 0x20000000;
    private static readonly Guid InputProcessorProfilesClassId =
        Guid.Parse("33C53A50-F456-4884-B049-85FD643ECFED");

    public static void Activate(WindowsInputMethodProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var type = Type.GetTypeFromCLSID(InputProcessorProfilesClassId, throwOnError: true)
            ?? throw new InvalidOperationException("Windows TSF 输入法服务不可用。" );
        var instance = System.Activator.CreateInstance(type)
            ?? throw new InvalidOperationException("无法创建 Windows TSF 输入法服务。" );
        try
        {
            var manager = (ITfInputProcessorProfileMgr)instance;
            var classId = profile.TipClassId;
            var profileId = profile.ProfileId;
            var result = manager.ActivateProfile(
                TfProfileTypeInputProcessor,
                profile.LanguageId,
                in classId,
                in profileId,
                IntPtr.Zero,
                TfIppmfForSession | TfIppmfDontCareCurrentInputLanguage);
            Marshal.ThrowExceptionForHR(result);
        }
        finally
        {
            if (Marshal.IsComObject(instance))
            {
                Marshal.FinalReleaseComObject(instance);
            }
        }
    }

    [ComImport]
    [Guid("71C6E74C-0F28-11D8-A82A-00065B84435C")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ITfInputProcessorProfileMgr
    {
        [PreserveSig]
        int ActivateProfile(
            uint profileType,
            ushort languageId,
            [In] ref readonly Guid classId,
            [In] ref readonly Guid profileGuid,
            IntPtr keyboardLayout,
            uint flags);
    }
}
