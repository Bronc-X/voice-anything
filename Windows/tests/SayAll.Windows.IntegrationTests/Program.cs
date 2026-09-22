using Windows.Media;
using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Xml.Linq;
using SayAll.Setup;
using SayAll.Windows;

if (args.Contains("--live-typeless-transcription", StringComparer.Ordinal))
{
    var typeless = WindowsVoiceInputCatalog.GetAvailableProfiles()
        .Single(profile => profile.Kind == WindowsVoiceInputKind.Typeless);
    WindowsVoiceInputActivator.Start(typeless);
    Console.WriteLine("LIVE Typeless transcription shortcut sent");
    return;
}

using var frame = new AudioFrame(4);
PcmAudioFrameWriter.Write(frame, [short.MinValue, short.MaxValue]);

Console.WriteLine("PASS WinRT AudioFrame PCM buffer access");

var logDirectory = Path.Combine(Path.GetTempPath(), "SayAll-BridgeLogTests");
var firstSession = HidBridgeSessionLogPath.Create(
    logDirectory,
    processId: 100,
    startedAt: new DateTimeOffset(2026, 8, 26, 18, 0, 0, TimeSpan.FromHours(8)),
    sessionId: Guid.Parse("11111111-1111-1111-1111-111111111111"));
var secondSession = HidBridgeSessionLogPath.Create(
    logDirectory,
    processId: 100,
    startedAt: new DateTimeOffset(2026, 8, 26, 18, 0, 1, TimeSpan.FromHours(8)),
    sessionId: Guid.Parse("22222222-2222-2222-2222-222222222222"));
if (firstSession == secondSession ||
    Path.GetDirectoryName(firstSession) != logDirectory ||
    Path.GetExtension(firstSession) != ".jsonl")
{
    throw new InvalidOperationException("HID bridge sessions must use distinct JSONL logs");
}

Console.WriteLine("PASS locked prior HID log cannot block a new bridge session");

var installerDirectory = Path.Combine(Path.GetTempPath(), "SayAll-InstallerTests");
Directory.CreateDirectory(installerDirectory);
var pinnedSource = Path.Combine(installerDirectory, "pinned-source.dll");
var lockedDestination = Path.Combine(installerDirectory, "pinned-destination.dll");
await File.WriteAllBytesAsync(pinnedSource, [0x52, 0x43, 0x30, 0x30, 0x33]);
File.Copy(pinnedSource, lockedDestination, overwrite: true);
var pinnedHash = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(pinnedSource)));
using (File.Open(lockedDestination, FileMode.Open, FileAccess.Read, FileShare.Read))
{
    var copied = PinnedFileInstaller.InstallOrReuse(
        pinnedSource,
        lockedDestination,
        expectedLength: 5,
        expectedSha256: pinnedHash);
    if (copied)
    {
        throw new InvalidOperationException(
            "A locked destination with the exact pinned fingerprint must be reused");
    }
}

Console.WriteLine("PASS locked verified HID runtime is reused during installation");

var retrySource = Path.Combine(installerDirectory, "retry-source.exe");
var retryDestination = Path.Combine(installerDirectory, "retry-destination.exe");
await File.WriteAllTextAsync(retrySource, "new-helper");
await File.WriteAllTextAsync(retryDestination, "old-helper");
using (var heldDestination = File.Open(
    retryDestination,
    FileMode.Open,
    FileAccess.Read,
    FileShare.Read))
{
    var releaseTask = Task.Run(async () =>
    {
        await Task.Delay(250);
        heldDestination.Dispose();
    });
    LockedFileCopy.CopyWithRetry(
        retrySource,
        retryDestination,
        overwrite: true,
        timeout: TimeSpan.FromSeconds(2));
    await releaseTask;
}
if (await File.ReadAllTextAsync(retryDestination) != "new-helper")
{
    throw new InvalidOperationException(
        "Installer must wait briefly for a stopping service to release its executable");
}

Console.WriteLine("PASS installer waits for a stopping service file lock to clear");

var playbackDirectory = Path.Combine(
    Path.GetTempPath(),
    "VoiceAnything-PlaybackTests",
    Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(playbackDirectory);
var playbackPath = Path.Combine(playbackDirectory, "rc003ms-local-test.wav");
await File.WriteAllBytesAsync(
    playbackPath,
    SayAll.Core.Audio.PcmWaveFile.BuildMono16([0, 4000, -4000, 0], 16_000));
using (var player = new LocalVoicePlayer())
{
    var source = player.Load(playbackPath);
    if (!source.IsFile ||
        !string.Equals(source.LocalPath, Path.GetFullPath(playbackPath), StringComparison.OrdinalIgnoreCase) ||
        player.UsesExternalProcess)
    {
        throw new InvalidOperationException(
            "Voice playback must stay inside SayAll and use the local WAV file directly");
    }
}

Console.WriteLine("PASS captured RC003MS WAV loads in the in-app local player");

var settingsPath = Path.Combine(playbackDirectory, "settings.json");
var settingsStore = new SayAllSettingsStore(settingsPath);
var expectedSettings = new SayAllSettings
{
    ConfigurationVersion = SayAllSettingsMigration.CurrentVersion,
    PreferredInputMethodId =
        "0804:{86598FB9-66A2-463E-B9C2-AEB906D477AD}{607FDF85-FCC8-4DBD-A365-41296F980C9C}",
    ActivePreset = "Codex",
    RemoteActionsEnabled = true,
    RemoteBindings = SayAll.Core.Input.RemoteBindingPresets.CreateCodex().Bindings.ToList(),
};
await settingsStore.SaveAsync(expectedSettings);
var restoredSettings = await settingsStore.LoadAsync();
if (restoredSettings.PreferredInputMethodId != expectedSettings.PreferredInputMethodId ||
    restoredSettings.ActivePreset != "Codex" ||
    !restoredSettings.RemoteActionsEnabled ||
    restoredSettings.RemoteBindings.Count != expectedSettings.RemoteBindings.Count)
{
    throw new InvalidOperationException(
        "Input method and clicked RC003MS bindings must survive an app restart");
}

Console.WriteLine("PASS input method and RC003MS bindings persist locally");

var spectrumPaletteType = typeof(MainWindow).Assembly.GetType(
    "SayAll.Windows.SpectrumPalette");
var spectrumColorizerType = typeof(MainWindow).Assembly.GetType(
    "SayAll.Windows.SpectrumPaletteColorizer");
var settingsPaletteProperty = typeof(SayAllSettings).GetProperty("SpectrumPalette");
if (spectrumPaletteType is null ||
    spectrumColorizerType is null ||
    settingsPaletteProperty is null ||
    !new[] { "Blue", "Ember", "Emerald" }.All(name =>
        Enum.GetNames(spectrumPaletteType).Contains(name, StringComparer.Ordinal)))
{
    throw new InvalidOperationException(
        "The live spectrum must expose selectable Blue, Ember, and Emerald palettes");
}

var colorizeSpectrum = spectrumColorizerType.GetMethod(
    "Colorize",
    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
if (colorizeSpectrum is null)
{
    throw new InvalidOperationException(
        "The live spectrum palette must expose deterministic color mapping");
}

object ColorizeSpectrum(string paletteName)
{
    var palette = Enum.Parse(spectrumPaletteType, paletteName);
    return colorizeSpectrum.Invoke(null, [palette, 0.72d])
        ?? throw new InvalidOperationException("Spectrum colorizer returned no color");
}

byte SpectrumChannel(object color, string channel) =>
    (byte)(color.GetType().GetProperty(channel)?.GetValue(color)
        ?? throw new InvalidOperationException($"Spectrum color is missing {channel}"));

var blueSpectrumColor = ColorizeSpectrum("Blue");
var emberSpectrumColor = ColorizeSpectrum("Ember");
var emeraldSpectrumColor = ColorizeSpectrum("Emerald");
if (SpectrumChannel(blueSpectrumColor, "Blue") <= SpectrumChannel(blueSpectrumColor, "Red") ||
    SpectrumChannel(emberSpectrumColor, "Red") <= SpectrumChannel(emberSpectrumColor, "Blue") ||
    SpectrumChannel(emeraldSpectrumColor, "Green") <= SpectrumChannel(emeraldSpectrumColor, "Red"))
{
    throw new InvalidOperationException(
        "Blue, Ember, and Emerald spectrum palettes must remain visibly distinct");
}

var paletteSettingsPath = Path.Combine(playbackDirectory, "palette-settings.json");
var paletteSettingsStore = new SayAllSettingsStore(paletteSettingsPath);
var paletteSettings = new SayAllSettings
{
    ConfigurationVersion = SayAllSettingsMigration.CurrentVersion,
};
var emeraldPalette = Enum.Parse(spectrumPaletteType, "Emerald");
settingsPaletteProperty.SetValue(paletteSettings, emeraldPalette);
await paletteSettingsStore.SaveAsync(paletteSettings);
var restoredPaletteSettings = await paletteSettingsStore.LoadAsync();
if (!Equals(settingsPaletteProperty.GetValue(restoredPaletteSettings), emeraldPalette))
{
    throw new InvalidOperationException(
        "The selected live spectrum palette must survive an app restart");
}

Console.WriteLine("PASS selectable live spectrum palettes persist locally");

var singleInstanceName = $"Local\\VoiceAnything.Integration.{Guid.NewGuid():N}";
using (var firstInstance = VoiceAnythingSingleInstance.TryAcquire(singleInstanceName))
using (var duplicateInstance = VoiceAnythingSingleInstance.TryAcquire(singleInstanceName))
{
    if (firstInstance is null || duplicateInstance is not null)
    {
        throw new InvalidOperationException(
            "Only one VoiceAnything process may consume the HID event log and execute configured actions");
    }
}
using (var restartedInstance = VoiceAnythingSingleInstance.TryAcquire(singleInstanceName))
{
    if (restartedInstance is null)
    {
        throw new InvalidOperationException(
            "The VoiceAnything single-instance lock must be released after a clean exit");
    }
}

Console.WriteLine("PASS one Windows session has exactly one VoiceAnything action executor");

var trayClosePolicyType = typeof(MainWindow).Assembly.GetType(
    "SayAll.Windows.TrayClosePolicy");
var trayClosePolicy = trayClosePolicyType is null
    ? null
    : Activator.CreateInstance(trayClosePolicyType);
var shouldCancelClose = trayClosePolicyType?.GetProperty("ShouldCancelClose");
var requestExit = trayClosePolicyType?.GetMethod("RequestExit");
if (trayClosePolicy is null ||
    shouldCancelClose?.GetValue(trayClosePolicy) is not true ||
    requestExit is null)
{
    throw new InvalidOperationException(
        "VoiceAnything must keep running when the main window close button is clicked");
}

requestExit.Invoke(trayClosePolicy, null);
if (shouldCancelClose.GetValue(trayClosePolicy) is not false)
{
    throw new InvalidOperationException(
        "Only an explicit tray exit may allow VoiceAnything to close completely");
}

var appProjectPath = FindRepositoryFile(
    "Windows", "src", "SayAll.Windows", "SayAll.Windows.csproj");
var setupProjectPath = FindRepositoryFile(
    "Windows", "src", "SayAll.Setup", "SayAll.Setup.csproj");
var appXamlPath = FindRepositoryFile(
    "Windows", "src", "SayAll.Windows", "App.xaml");
var appCodePath = FindRepositoryFile(
    "Windows", "src", "SayAll.Windows", "App.xaml.cs");
var appProject = XDocument.Load(appProjectPath);
var setupProject = XDocument.Load(setupProjectPath);
var appXaml = XDocument.Load(appXamlPath);
var appCode = await File.ReadAllTextAsync(appCodePath);
var appIconSetting = appProject.Descendants("ApplicationIcon").SingleOrDefault()?.Value;
var setupIconSetting = setupProject.Descendants("ApplicationIcon").SingleOrDefault()?.Value;
var iconPath = string.IsNullOrWhiteSpace(appIconSetting)
    ? string.Empty
    : Path.GetFullPath(Path.Combine(
        Path.GetDirectoryName(appProjectPath)!,
        appIconSetting));
var iconBytes = File.Exists(iconPath)
    ? await File.ReadAllBytesAsync(iconPath)
    : [];
var iconImageCount = iconBytes.Length >= 6
    ? BinaryPrimitives.ReadUInt16LittleEndian(iconBytes.AsSpan(4, 2))
    : 0;
var appRoot = appXaml.Root;
if (appProject.Descendants("UseWindowsForms").SingleOrDefault()?.Value != "true" ||
    string.IsNullOrWhiteSpace(appIconSetting) ||
    setupIconSetting != appIconSetting ||
    iconBytes.Length < 6 ||
    iconBytes[0] != 0 ||
    iconBytes[1] != 0 ||
    iconBytes[2] != 1 ||
    iconBytes[3] != 0 ||
    iconImageCount < 4 ||
    appRoot?.Attribute("StartupUri") is not null ||
    (string?)appRoot?.Attribute("ShutdownMode") != "OnExplicitShutdown")
{
    throw new InvalidOperationException(
        "VoiceAnything must ship one multi-size icon and use explicit shutdown for tray operation");
}

if (!appCode.Contains("System.Windows.Forms.NotifyIcon", StringComparison.Ordinal) ||
    !appCode.Contains("打开 VoiceAnything", StringComparison.Ordinal) ||
    !appCode.Contains("完全退出", StringComparison.Ordinal) ||
    !appCode.Contains("mainWindow.Hide()", StringComparison.Ordinal) ||
    !appCode.Contains("ShowMainWindow", StringComparison.Ordinal))
{
    throw new InvalidOperationException(
        "VoiceAnything must expose tray actions for restoring the window and exiting completely");
}

Console.WriteLine("PASS VoiceAnything app icon and close-to-tray lifecycle");

var legacySettingsPath = Path.Combine(playbackDirectory, "SayAll", "settings.json");
var vibeControlSettingsPath = Path.Combine(playbackDirectory, "VoiceAnything", "settings.json");
await new SayAllSettingsStore(legacySettingsPath).SaveAsync(expectedSettings);
var migrationConstructor = typeof(SayAllSettingsStore).GetConstructor(
    [typeof(string), typeof(string)]);
if (migrationConstructor is null)
{
    throw new InvalidOperationException(
        "VoiceAnything settings must accept one legacy SayAll migration source");
}
var migrationStore = (SayAllSettingsStore)migrationConstructor.Invoke(
    [vibeControlSettingsPath, legacySettingsPath]);
var migratedSettings = await migrationStore.LoadAsync();
if (!File.Exists(vibeControlSettingsPath) ||
    migratedSettings.PreferredInputMethodId != expectedSettings.PreferredInputMethodId ||
    migratedSettings.RemoteBindings.Count != expectedSettings.RemoteBindings.Count)
{
    throw new InvalidOperationException(
        "VoiceAnything must migrate the existing SayAll input method and remote bindings once");
}

Console.WriteLine("PASS VoiceAnything migrates the existing SayAll user configuration");

var sharedBridgeLog = HidBridgeRuntimePaths.SharedEventLogPath;
if (!Path.IsPathFullyQualified(sharedBridgeLog) ||
    !sharedBridgeLog.StartsWith(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        StringComparison.OrdinalIgnoreCase))
{
    throw new InvalidOperationException(
        "The no-popup background HID bridge must expose a stable ProgramData event log");
}

Console.WriteLine("PASS background HID bridge uses one stable machine-wide endpoint");

if (InputDispatchPolicy.GestureFlushInterval > TimeSpan.FromMilliseconds(50) ||
    InputDispatchPolicy.MaximumSingleClickDispatchDelay > TimeSpan.FromMilliseconds(400))
{
    throw new InvalidOperationException(
        "RC003MS input dispatch must not wait for the one-second device-status refresh");
}

Console.WriteLine("PASS RC003MS button dispatch stays below the perceptible one-second delay");

var requiredCodexActions = new[]
{
    "ContextUp",
    "ContextDown",
    "PreviousProject",
    "NextProject",
    "SelectReasoning",
    "NewChat",
    "NewStandaloneChat",
    "PreviousChat",
    "NextChat",
    "NextChatNeedingAttention",
    "OpenCommandMenu",
    "OpenProjectTools",
    "OpenProjectPicker",
    "ToggleSidebar",
    "ToggleTerminal",
    "SearchFiles",
    "ToggleFileTree",
    "OpenReview",
    "ToggleReviewPanel",
    "OpenBrowser",
    "OpenSettings",
    "ApproveRequest",
    "DeclineRequest",
};
foreach (var actionName in requiredCodexActions)
{
    if (!Enum.TryParse<SayAll.Core.Input.RemoteButtonAction>(actionName, out _))
    {
        throw new InvalidOperationException(
            $"The RC003MS action library is missing the official Codex command: {actionName}");
    }
}

Console.WriteLine("PASS official Codex Windows actions are available to every RC003MS key");

var shortcutCatalogType = typeof(RemoteActionExecutor).Assembly.GetType(
    "SayAll.Windows.RemoteActionShortcutCatalog");
if (shortcutCatalogType is null)
{
    throw new InvalidOperationException(
        "The official Codex Windows shortcuts must have one testable catalog");
}
var resolveShortcut = shortcutCatalogType.GetMethod("Resolve");
if (resolveShortcut is null)
{
    throw new InvalidOperationException("The Codex shortcut catalog must expose Resolve");
}
var expectedShortcuts = new Dictionary<string, (ushort Key, ushort[] Modifiers)>
{
    ["GoBack"] = (0xDB, [0x11]),
    ["NewChat"] = (0x4E, [0x11]),
    ["NewStandaloneChat"] = (0x4F, [0x11, 0x12]),
    ["PreviousChat"] = (0x21, [0x11]),
    ["NextChat"] = (0x22, [0x11]),
    ["NextChatNeedingAttention"] = (0x41, [0x11, 0x12]),
    ["SubmitTask"] = (0x0D, [0x11]),
    ["SelectModel"] = (0x4D, [0x11, 0x10]),
    ["OpenCommandMenu"] = (0x27, [0x11, 0x12]),
    ["OpenProjectPicker"] = (0x4F, [0x11, 0x12, 0x10]),
    ["ToggleSidebar"] = (0x42, [0x11]),
    ["ToggleTerminal"] = (0xC0, [0x11]),
    ["SearchFiles"] = (0x50, [0x11]),
    ["ToggleFileTree"] = (0x45, [0x11, 0x10]),
    ["OpenReview"] = (0x47, [0x11, 0x10]),
    ["ToggleReviewPanel"] = (0x42, [0x11, 0x12]),
    ["OpenBrowser"] = (0x54, [0x11]),
    ["OpenSettings"] = (0xBC, [0x11]),
    ["ApproveRequest"] = (0x0D, []),
    ["DeclineRequest"] = (0x1B, []),
};
foreach (var expected in expectedShortcuts)
{
    var action = Enum.Parse<SayAll.Core.Input.RemoteButtonAction>(expected.Key);
    var shortcut = resolveShortcut.Invoke(null, [action]);
    if (shortcut is null)
    {
        throw new InvalidOperationException($"Missing Codex shortcut for {expected.Key}");
    }
    var shortcutType = shortcut.GetType();
    var key = (ushort)(shortcutType.GetProperty("Key")?.GetValue(shortcut)
        ?? throw new InvalidOperationException("Shortcut key is not inspectable"));
    var modifiers = (IReadOnlyList<ushort>)(shortcutType.GetProperty("Modifiers")?.GetValue(shortcut)
        ?? throw new InvalidOperationException("Shortcut modifiers are not inspectable"));
    if (key != expected.Value.Key || !modifiers.SequenceEqual(expected.Value.Modifiers))
    {
        throw new InvalidOperationException($"Unexpected Windows shortcut for {expected.Key}");
    }
}

Console.WriteLine("PASS official Codex Windows shortcuts match the documented commands");

var reasoningShortcut = resolveShortcut.Invoke(
    null,
    [SayAll.Core.Input.RemoteButtonAction.SelectReasoning]);
var reasoningKey = (ushort?)(reasoningShortcut?.GetType()
    .GetProperty("Key")?.GetValue(reasoningShortcut));
var reasoningModifiers = (IReadOnlyList<ushort>?)reasoningShortcut?.GetType()
    .GetProperty("Modifiers")?.GetValue(reasoningShortcut);
if (reasoningKey != 0x7B ||
    reasoningModifiers is null ||
    !reasoningModifiers.SequenceEqual([(ushort)0x11, (ushort)0x12, (ushort)0x10]))
{
    throw new InvalidOperationException(
        "VoiceAnything must reserve Ctrl+Alt+Shift+F12 for Codex reasoning selection");
}

Console.WriteLine("PASS VoiceAnything exposes one collision-resistant Codex reasoning shortcut");

var keybindingPath = Path.Combine(playbackDirectory, "keybindings.json");
await File.WriteAllTextAsync(
    keybindingPath,
    """
    [
      { "command": "composer.togglePlanMode", "key": "Ctrl+Alt+P" },
      { "command": "globalDictationToggle", "key": "Ctrl+Alt+Shift+F11" }
    ]
    """);
var keybindingsChanged = CodexKeybindingProvisioner.EnsureReasoningShortcut(keybindingPath);
var keybindingsJson = await File.ReadAllTextAsync(keybindingPath);
using (var keybindingsDocument = System.Text.Json.JsonDocument.Parse(keybindingsJson))
{
    var bindings = keybindingsDocument.RootElement.EnumerateArray().ToArray();
    if (!keybindingsChanged ||
        !bindings.Any(binding =>
            binding.GetProperty("command").GetString() == "composer.togglePlanMode" &&
            binding.GetProperty("key").GetString() == "Ctrl+Alt+P") ||
        bindings.Count(binding =>
            binding.GetProperty("command").GetString() == "togglePriorityFilter" &&
            binding.GetProperty("key").GetString() == "Ctrl+Alt+Right") != 1 ||
        bindings.Count(binding =>
            binding.GetProperty("command").GetString() ==
                CodexKeybindingProvisioner.ReasoningCommandId &&
            binding.GetProperty("key").GetString() ==
                CodexKeybindingProvisioner.ReasoningAccelerator) != 1 ||
        bindings.Any(binding =>
            binding.GetProperty("command").GetString() ==
                "globalDictationToggle" &&
            binding.GetProperty("key").GetString() ==
                "Ctrl+Alt+Shift+F11"))
    {
        throw new InvalidOperationException(
            "Provisioning must preserve user shortcuts, append reasoning once, and remove the obsolete global dictation binding");
    }
}
if (CodexKeybindingProvisioner.EnsureReasoningShortcut(keybindingPath))
{
    throw new InvalidOperationException(
        "Reasoning shortcut provisioning must be idempotent");
}

Console.WriteLine("PASS Codex activity and reasoning shortcuts merge without overwriting user shortcuts");

var navigationState = new RemoteNavigationState(TimeSpan.FromSeconds(10));
var navigationStartedAt = new DateTimeOffset(2026, 8, 28, 10, 0, 0, TimeSpan.FromHours(8));
if (navigationState.Resolve(
        SayAll.Core.Input.RemoteButtonAction.ContextUp,
        navigationStartedAt).ToString() != "ScrollProjectUp")
{
    throw new InvalidOperationException(
        "Up must scroll the current project conversation when no picker is open");
}
navigationState.EnterOperation(navigationStartedAt);
if (navigationState.Resolve(
        SayAll.Core.Input.RemoteButtonAction.ContextUp,
        navigationStartedAt.AddSeconds(1)) != RemoteNavigationDecision.NavigateUp ||
    navigationState.Resolve(
        SayAll.Core.Input.RemoteButtonAction.ContextDown,
        navigationStartedAt.AddSeconds(1)) != RemoteNavigationDecision.NavigateDown ||
    navigationState.Resolve(
        SayAll.Core.Input.RemoteButtonAction.ContextDown,
        navigationStartedAt.AddSeconds(11)).ToString() != "ScrollProjectDown")
{
    throw new InvalidOperationException(
        "Up and Down must navigate a recent picker, then return to project scrolling after it closes or expires");
}

Console.WriteLine("PASS Up and Down use context navigation without losing default project scrolling");

var activityNavigation = new RemoteNavigationState(TimeSpan.FromSeconds(10));
activityNavigation.EnterOperation(navigationStartedAt);
var activityMoves = new[]
{
    activityNavigation.Resolve(
        SayAll.Core.Input.RemoteButtonAction.PreviousChat,
        navigationStartedAt.AddSeconds(1)),
    activityNavigation.Resolve(
        SayAll.Core.Input.RemoteButtonAction.NextChat,
        navigationStartedAt.AddSeconds(2)),
    activityNavigation.Resolve(
        SayAll.Core.Input.RemoteButtonAction.PreviousProject,
        navigationStartedAt.AddSeconds(3)),
    activityNavigation.Resolve(
        SayAll.Core.Input.RemoteButtonAction.NextProject,
        navigationStartedAt.AddSeconds(4)),
};
if (activityMoves[0] != RemoteNavigationDecision.NavigateUp ||
    activityMoves[1] != RemoteNavigationDecision.NavigateDown ||
    activityMoves[2] != RemoteNavigationDecision.NavigateUp ||
    activityMoves[3] != RemoteNavigationDecision.NavigateDown)
{
    throw new InvalidOperationException(
        "Activity must make Left and Right visibly move the running-project highlight");
}

var activityConfirm = activityNavigation.Resolve(
    SayAll.Core.Input.RemoteButtonAction.SubmitTask,
    navigationStartedAt.AddSeconds(5));
if (activityConfirm.ToString() != "ConfirmSelection" ||
    activityNavigation.Resolve(
        SayAll.Core.Input.RemoteButtonAction.NextChat,
        navigationStartedAt.AddSeconds(6)).ToString() != "ScrollProjectDown")
{
    throw new InvalidOperationException(
        "OK must confirm the selected running project and leave Activity mode");
}

activityNavigation.EnterOperation(navigationStartedAt.AddSeconds(7));
var activityDismiss = activityNavigation.Resolve(
    SayAll.Core.Input.RemoteButtonAction.GoBack,
    navigationStartedAt.AddSeconds(8));
if (activityDismiss.ToString() != "DismissOperation" ||
    activityNavigation.Resolve(
        SayAll.Core.Input.RemoteButtonAction.PreviousProject,
        navigationStartedAt.AddSeconds(9)) != RemoteNavigationDecision.None)
{
    throw new InvalidOperationException(
        "Back must close Activity mode without replacing its normal outside-Activity behavior");
}

var activityLifetime = new RemoteNavigationState(TimeSpan.FromSeconds(10));
activityLifetime.EnterOperation(navigationStartedAt);
_ = activityLifetime.Resolve(
    SayAll.Core.Input.RemoteButtonAction.NextChat,
    navigationStartedAt.AddSeconds(9));
if (activityLifetime.Resolve(
        SayAll.Core.Input.RemoteButtonAction.NextChat,
        navigationStartedAt.AddSeconds(18)) != RemoteNavigationDecision.NavigateDown)
{
    throw new InvalidOperationException(
        "Each Activity navigation press must keep the running-project selector active");
}

Console.WriteLine("PASS Activity view supports running-project movement, confirmation, exit, and continued navigation");

var previousProjectPlan = CodexProjectSwitchPlan.Create(previous: true);
var nextProjectPlan = CodexProjectSwitchPlan.Create(previous: false);
if (previousProjectPlan.Count != 3 || nextProjectPlan.Count != 3 ||
    previousProjectPlan[0].Key != 0x4F ||
    !previousProjectPlan[0].Modifiers.SequenceEqual([(ushort)0x11, (ushort)0x12, (ushort)0x10]) ||
    previousProjectPlan[1].Key != 0x26 || nextProjectPlan[1].Key != 0x28 ||
    previousProjectPlan[2].Key != 0x0D || nextProjectPlan[2].Key != 0x0D)
{
    throw new InvalidOperationException(
        "Left and Right must open the official project picker, move once, and confirm");
}
if (CodexProjectSwitchPlan.PickerOpenDelay < TimeSpan.FromMilliseconds(80) ||
    CodexProjectSwitchPlan.PickerOpenDelay > TimeSpan.FromMilliseconds(200))
{
    throw new InvalidOperationException(
        "The Codex project picker must get a short render window before Left or Right selects an item");
}

Console.WriteLine("PASS Left and Right switch projects through the official Codex picker");

var previousRunningItem = CodexRunningItemSwitchPlan.Create(previous: true);
var nextRunningItem = CodexRunningItemSwitchPlan.Create(previous: false);
if (previousRunningItem.Key != 0x21 ||
    !previousRunningItem.Modifiers.SequenceEqual([(ushort)0x11]) ||
    nextRunningItem.Key != 0x22 ||
    !nextRunningItem.Modifiers.SequenceEqual([(ushort)0x11]))
{
    throw new InvalidOperationException(
        "Left and Right must use Codex's previous/next visible-thread shortcuts so activity filtering is respected");
}
if (CodexSidebarScrollPlan.Resolve(
        SayAll.Core.Input.RemoteButtonAction.PreviousChat) != 0 ||
    CodexSidebarScrollPlan.Resolve(
        SayAll.Core.Input.RemoteButtonAction.NextChat) != 0)
{
    throw new InvalidOperationException(
        "Up and Down must no longer move an unselected sidebar list");
}

Console.WriteLine("PASS Menu opens activity and directional keys navigate its visible running tasks");

var projectToolsAction = Enum.Parse<SayAll.Core.Input.RemoteButtonAction>("OpenProjectTools");
var projectToolsProfile = SayAll.Core.Input.RemoteBindingPresets.CreateCodex();
if (projectToolsProfile.Resolve(
        SayAll.Core.Input.RemoteButton.Tv,
        SayAll.Core.Input.RemoteButtonTrigger.SingleClick).Action != projectToolsAction)
{
    throw new InvalidOperationException(
        "TV must open the project tools layer instead of consuming a scarce key on one browser action");
}

var projectToolStateType = typeof(RemoteActionExecutor).Assembly.GetType(
    "SayAll.Windows.ProjectToolMenuState")
    ?? throw new InvalidOperationException("The project tools layer needs a testable menu state");
var projectToolState = Activator.CreateInstance(projectToolStateType)
    ?? throw new InvalidOperationException("The project tools state could not be created");
var openProjectTools = projectToolStateType.GetMethod("Open")
    ?? throw new InvalidOperationException("The project tools state must expose Open");
var moveProjectTools = projectToolStateType.GetMethod("Move")
    ?? throw new InvalidOperationException("The project tools state must expose Move");
var confirmProjectTool = projectToolStateType.GetMethod("Confirm")
    ?? throw new InvalidOperationException("The project tools state must expose Confirm");
var isProjectToolsOpen = projectToolStateType.GetProperty("IsOpen")
    ?? throw new InvalidOperationException("The project tools state must expose IsOpen");
var projectToolItems = projectToolStateType.GetProperty("Items")
    ?? throw new InvalidOperationException("The project tools state must expose Items");
openProjectTools.Invoke(projectToolState, null);
var tools = (System.Collections.ICollection)(projectToolItems.GetValue(projectToolState)
    ?? throw new InvalidOperationException("The project tools state returned no actions"));
if (!(bool)(isProjectToolsOpen.GetValue(projectToolState) ?? false) || tools.Count < 8)
{
    throw new InvalidOperationException(
        "The project tools layer must open with the core project actions visible");
}
moveProjectTools.Invoke(projectToolState, [1]);
var selectedProjectTool = confirmProjectTool.Invoke(projectToolState, null)
    ?? throw new InvalidOperationException("OK must return the selected project action");
var selectedProjectToolAction = selectedProjectTool.GetType().GetProperty("Action")
    ?.GetValue(selectedProjectTool)?.ToString();
if (selectedProjectToolAction != "ToggleTerminal" ||
    (bool)(isProjectToolsOpen.GetValue(projectToolState) ?? true))
{
    throw new InvalidOperationException(
        "Project tools must move predictably, confirm the highlighted action, and close");
}

Console.WriteLine("PASS TV opens a scalable project tools layer with deterministic selection");

var oldMappingSettings = new SayAllSettings
{
    ConfigurationVersion = 0,
    PreferredInputMethodId = WindowsVoiceInputCatalog.WeChatVoiceId,
    ActivePreset = "Codex",
    RemoteBindings = SayAll.Core.Input.RemoteBindingPresets.CreateClaudeCode()
        .WithBinding(
            SayAll.Core.Input.RemoteButton.Tv,
            SayAll.Core.Input.RemoteButtonTrigger.DoubleClick,
            new SayAll.Core.Input.RemoteButtonBinding(
                SayAll.Core.Input.RemoteButtonAction.InsertSkill,
                "$baseline-packager"))
        .Bindings.ToList(),
};
if (!SayAllSettingsMigration.Apply(oldMappingSettings) ||
    oldMappingSettings.ConfigurationVersion != SayAllSettingsMigration.CurrentVersion ||
    oldMappingSettings.PreferredInputMethodId != WindowsVoiceInputCatalog.TypelessId ||
    oldMappingSettings.RemoteBindings.Single(entry =>
        entry.Button == SayAll.Core.Input.RemoteButton.Back &&
        entry.Trigger == SayAll.Core.Input.RemoteButtonTrigger.SingleClick)
        .Binding.Action != SayAll.Core.Input.RemoteButtonAction.GoBack ||
    oldMappingSettings.RemoteBindings.Single(entry =>
        entry.Button == SayAll.Core.Input.RemoteButton.Tv &&
        entry.Trigger == SayAll.Core.Input.RemoteButtonTrigger.SingleClick)
        .Binding.Action != projectToolsAction ||
    oldMappingSettings.RemoteBindings.Single(entry =>
        entry.Button == SayAll.Core.Input.RemoteButton.Tv &&
        entry.Trigger == SayAll.Core.Input.RemoteButtonTrigger.DoubleClick)
        .Binding.Argument != "$baseline-packager" ||
    oldMappingSettings.RemoteBindings.Single(entry =>
        entry.Button == SayAll.Core.Input.RemoteButton.Up &&
        entry.Trigger == SayAll.Core.Input.RemoteButtonTrigger.SingleClick)
        .Binding.Action != SayAll.Core.Input.RemoteButtonAction.PreviousChat ||
    oldMappingSettings.RemoteBindings.Single(entry =>
        entry.Button == SayAll.Core.Input.RemoteButton.VolumeDown &&
        entry.Trigger == SayAll.Core.Input.RemoteButtonTrigger.SingleClick)
        .Binding.Action != SayAll.Core.Input.RemoteButtonAction.SelectReasoning)
{
    throw new InvalidOperationException(
        "Existing VoiceAnything settings must migrate the requested defaults while preserving unrelated custom gestures");
}

Console.WriteLine("PASS existing settings migrate to Typeless and the requested RC003MS mapping");

var versionOneSettings = new SayAllSettings
{
    ConfigurationVersion = 1,
    PreferredInputMethodId = WindowsVoiceInputCatalog.TypelessId,
    ActivePreset = "Codex",
    RemoteBindings = SayAll.Core.Input.RemoteBindingPresets.CreateCodex()
        .WithBinding(
            SayAll.Core.Input.RemoteButton.Back,
            SayAll.Core.Input.RemoteButtonTrigger.SingleClick,
            new SayAll.Core.Input.RemoteButtonBinding(
                SayAll.Core.Input.RemoteButtonAction.OpenBrowser))
        .WithBinding(
            SayAll.Core.Input.RemoteButton.Tv,
            SayAll.Core.Input.RemoteButtonTrigger.SingleClick,
            new SayAll.Core.Input.RemoteButtonBinding(
                SayAll.Core.Input.RemoteButtonAction.InsertSkill,
                "$"))
        .WithBinding(
            SayAll.Core.Input.RemoteButton.Tv,
            SayAll.Core.Input.RemoteButtonTrigger.DoubleClick,
            new SayAll.Core.Input.RemoteButtonBinding(
                SayAll.Core.Input.RemoteButtonAction.Disabled))
        .Bindings.ToList(),
};
if (!SayAllSettingsMigration.Apply(versionOneSettings) ||
    versionOneSettings.ConfigurationVersion != SayAllSettingsMigration.CurrentVersion ||
    versionOneSettings.RemoteBindings.Single(entry =>
        entry.Button == SayAll.Core.Input.RemoteButton.Back &&
        entry.Trigger == SayAll.Core.Input.RemoteButtonTrigger.SingleClick)
        .Binding.Action != SayAll.Core.Input.RemoteButtonAction.GoBack ||
    versionOneSettings.RemoteBindings.Single(entry =>
        entry.Button == SayAll.Core.Input.RemoteButton.Tv &&
        entry.Trigger == SayAll.Core.Input.RemoteButtonTrigger.SingleClick)
        .Binding.Action != projectToolsAction ||
    versionOneSettings.RemoteBindings.Single(entry =>
        entry.Button == SayAll.Core.Input.RemoteButton.Tv &&
        entry.Trigger == SayAll.Core.Input.RemoteButtonTrigger.DoubleClick)
        .Binding.Action != SayAll.Core.Input.RemoteButtonAction.InsertSkill ||
    versionOneSettings.RemoteBindings.Single(entry =>
        entry.Button == SayAll.Core.Input.RemoteButton.Down &&
        entry.Trigger == SayAll.Core.Input.RemoteButtonTrigger.SingleClick)
        .Binding.Action != SayAll.Core.Input.RemoteButtonAction.NextChat ||
    versionOneSettings.RemoteBindings.Single(entry =>
        entry.Button == SayAll.Core.Input.RemoteButton.VolumeUp &&
        entry.Trigger == SayAll.Core.Input.RemoteButtonTrigger.SingleClick)
        .Binding.Action != SayAll.Core.Input.RemoteButtonAction.SelectModel)
{
    throw new InvalidOperationException(
        "Version-one VoiceAnything settings must restore Back and move Open Browser to TV without losing skill insertion");
}

Console.WriteLine("PASS version-one settings migrate Back, TV, and skill insertion to their new gestures");

var versionTwoSettings = new SayAllSettings
{
    ConfigurationVersion = 2,
    PreferredInputMethodId = WindowsVoiceInputCatalog.TypelessId,
    ActivePreset = "Codex",
    RemoteBindings = SayAll.Core.Input.RemoteBindingPresets.CreateCodex().Bindings.ToList(),
};
if (!SayAllSettingsMigration.Apply(versionTwoSettings) ||
    versionTwoSettings.ConfigurationVersion != SayAllSettingsMigration.CurrentVersion ||
    versionTwoSettings.RemoteBindings.Single(entry =>
        entry.Button == SayAll.Core.Input.RemoteButton.Up &&
        entry.Trigger == SayAll.Core.Input.RemoteButtonTrigger.SingleClick)
        .Binding.Action != SayAll.Core.Input.RemoteButtonAction.PreviousChat ||
    versionTwoSettings.RemoteBindings.Single(entry =>
        entry.Button == SayAll.Core.Input.RemoteButton.Down &&
        entry.Trigger == SayAll.Core.Input.RemoteButtonTrigger.SingleClick)
        .Binding.Action != SayAll.Core.Input.RemoteButtonAction.NextChat ||
    versionTwoSettings.RemoteBindings.Single(entry =>
        entry.Button == SayAll.Core.Input.RemoteButton.VolumeUp &&
        entry.Trigger == SayAll.Core.Input.RemoteButtonTrigger.SingleClick)
        .Binding.Action != SayAll.Core.Input.RemoteButtonAction.SelectModel ||
    versionTwoSettings.RemoteBindings.Single(entry =>
        entry.Button == SayAll.Core.Input.RemoteButton.VolumeDown &&
        entry.Trigger == SayAll.Core.Input.RemoteButtonTrigger.SingleClick)
        .Binding.Action != SayAll.Core.Input.RemoteButtonAction.SelectReasoning)
{
    throw new InvalidOperationException(
        "Version-two VoiceAnything settings must migrate to the stable chat and in-function navigation layers");
}

Console.WriteLine("PASS version-two settings migrate to stable chat and in-function navigation");

var backShortcut = resolveShortcut.Invoke(
    null,
    [SayAll.Core.Input.RemoteButtonAction.GoBack]);
var backModifiers = (IReadOnlyList<ushort>?)backShortcut?.GetType()
    .GetProperty("Modifiers")?.GetValue(backShortcut);
var backKey = (ushort?)backShortcut?.GetType().GetProperty("Key")?.GetValue(backShortcut);
if (backShortcut is null || backModifiers is null || backKey != 0xDB ||
    !backModifiers.SequenceEqual([(ushort)0x11]))
{
    throw new InvalidOperationException(
        "The configured back action must call Codex Navigate Back with Ctrl+[");
}

Console.WriteLine("PASS configured back action calls the installed Codex Navigate Back command");

var resolveShortcutForProcess = shortcutCatalogType.GetMethod("ResolveForProcess");
var newChatAction = Enum.Parse<SayAll.Core.Input.RemoteButtonAction>("NewChat");
if (resolveShortcutForProcess is null ||
    resolveShortcutForProcess.Invoke(null, [newChatAction, "ChatGPT"]) is null ||
    resolveShortcutForProcess.Invoke(null, [newChatAction, "WindowsTerminal"]) is not null ||
    resolveShortcutForProcess.Invoke(null, [newChatAction, "Claude"]) is not null)
{
    throw new InvalidOperationException(
        "Codex desktop commands must run in ChatGPT.exe without leaking into terminals or Claude");
}

Console.WriteLine("PASS Codex-only shortcuts stay inside the installed desktop app");

var targetCatalogType = typeof(RemoteActionExecutor).Assembly.GetType(
    "SayAll.Windows.RemoteActionTargetCatalog");
var supportsProcess = targetCatalogType?.GetMethod("SupportsProcess");
if (supportsProcess is null ||
    supportsProcess.Invoke(null, ["ChatGPT"]) is not true ||
    supportsProcess.Invoke(null, ["notepad"]) is not false)
{
    throw new InvalidOperationException(
        "Installed Codex runs as ChatGPT.exe and must be accepted without allowing arbitrary apps");
}

Console.WriteLine("PASS installed ChatGPT Codex process is an authenticated action target");

var inputMethods = WindowsInputMethodCatalog.GetConfiguredProfiles();
if (inputMethods.Any(profile =>
        profile.TipClassId == Guid.Empty || profile.ProfileId == Guid.Empty))
{
    throw new InvalidOperationException(
        "Keyboard-layout placeholders must not appear as selectable TSF input methods");
}
var weType = inputMethods.SingleOrDefault(profile =>
    profile.Id.Equals(
        "0804:{86598FB9-66A2-463E-B9C2-AEB906D477AD}{607FDF85-FCC8-4DBD-A365-41296F980C9C}",
        StringComparison.OrdinalIgnoreCase));
if (weType is not null && (weType.DisplayName != "WeType" || weType.LanguageId != 0x0804))
{
    throw new InvalidOperationException(
        "SayAll must enumerate the WeType profile configured on this Windows account");
}

// A clean Windows account may have no third-party IME installed. Verify the TSF
// parser with a fixed public profile ID, and every profile actually returned by Windows.
foreach (var method in inputMethods)
{
    var parsed = WindowsInputMethodProfile.Parse(method.Id);
    if (parsed.TipClassId != method.TipClassId || parsed.ProfileId != method.ProfileId || parsed.LanguageId != method.LanguageId)
        throw new InvalidOperationException("An enumerated TSF profile did not round-trip.");
}
var parsedInputMethod = WindowsInputMethodProfile.Parse("0804:{86598FB9-66A2-463E-B9C2-AEB906D477AD}{607FDF85-FCC8-4DBD-A365-41296F980C9C}");
if (parsedInputMethod.TipClassId != Guid.Parse("86598FB9-66A2-463E-B9C2-AEB906D477AD") ||
    parsedInputMethod.ProfileId != Guid.Parse("607FDF85-FCC8-4DBD-A365-41296F980C9C"))
{
    throw new InvalidOperationException(
        "The persisted input method ID must round-trip into the exact TSF profile");
}

Console.WriteLine("PASS configured Windows input methods are available without a CLI");

var voiceInputs = WindowsVoiceInputCatalog.GetAvailableProfiles();
var voiceInputNames = voiceInputs.Select(profile => profile.DisplayName).ToArray();
if (!voiceInputNames.SequenceEqual(["Typeless", "Codex 语音听写", "微信语音输入法", "网易叭哥说"]) ||
    new SayAllSettings().PreferredInputMethodId != WindowsVoiceInputCatalog.TypelessId)
{
    throw new InvalidOperationException(
        $"VoiceAnything must keep Typeless as the explicit default without hiding the Codex option, got: {string.Join("、", voiceInputNames)}");
}
if (voiceInputs.Any(profile => string.IsNullOrWhiteSpace(profile.Id)))
{
    throw new InvalidOperationException("Every selectable voice input must have a persistent ID");
}

Console.WriteLine("PASS Typeless is the protected default while Codex dictation remains selectable");

var readyTypeless = VoiceInputReadinessPolicy.Evaluate(
    WindowsVoiceInputKind.Typeless,
    executableInstalled: true,
    appRunning: true,
    helperRunning: false);
var stoppedTypeless = VoiceInputReadinessPolicy.Evaluate(
    WindowsVoiceInputKind.Typeless,
    executableInstalled: true,
    appRunning: false,
    helperRunning: false);
var readyBageShuo = VoiceInputReadinessPolicy.Evaluate(
    WindowsVoiceInputKind.BageShuo,
    executableInstalled: true,
    appRunning: true,
    helperRunning: true);
var missingBageShuo = VoiceInputReadinessPolicy.Evaluate(
    WindowsVoiceInputKind.BageShuo,
    executableInstalled: false,
    appRunning: false,
    helperRunning: false);
if (!readyTypeless.CanTest ||
    !readyTypeless.StatusText.Contains("可测试", StringComparison.Ordinal) ||
    stoppedTypeless.CanTest ||
    !readyBageShuo.CanTest ||
    !readyBageShuo.StatusText.Contains("已连接", StringComparison.Ordinal) ||
    missingBageShuo.CanTest)
{
    throw new InvalidOperationException(
        "The home page must distinguish installed, running, connected, and unavailable voice inputs");
}

Console.WriteLine("PASS voice-input readiness states only enable viable self-tests");

if (!SayAll.HidBridge.Contracts.Rc003DriverContract.IsSupported(
        "Microsoft.Bluetooth.Profiles.HidOverGatt.dll",
        233_472,
        "372C3628E3366C18152199EB8FB554D27BB4F02D1356B1F514F711328DF4A8D6"))
{
    throw new InvalidOperationException(
        "The current signed Windows 11 HidOverGatt driver must pass the fingerprint gate");
}

Console.WriteLine("PASS current signed HidOverGatt driver fingerprint is supported");

var verifiedRuntimePath = FindRepositoryFile(
    "Windows", "src", "SayAll.HidBridge", "VerifiedRuntime.cs");
var verifiedRuntimeCode = await File.ReadAllTextAsync(verifiedRuntimePath);
if (!verifiedRuntimeCode.Contains(
        "Rc003DriverContract.IsSupported(",
        StringComparison.Ordinal) ||
    verifiedRuntimeCode.Contains(
        "Rc003DriverContract.DriverSha256,\r\n            \"HidOverGatt driver\"",
        StringComparison.Ordinal))
{
    throw new InvalidOperationException(
        "The installed HID service must use the complete driver fingerprint allowlist");
}

Console.WriteLine("PASS HID service runtime uses the complete driver fingerprint allowlist");

var codexVoice = voiceInputs.Single(profile => profile.Kind == WindowsVoiceInputKind.CodexDictation);
var typelessVoice = voiceInputs.Single(profile => profile.Kind == WindowsVoiceInputKind.Typeless);
var weChatVoice = voiceInputs.Single(profile => profile.Kind == WindowsVoiceInputKind.WeChatVoice);
var bageShuoVoice = voiceInputs.Single(profile => profile.Kind == WindowsVoiceInputKind.BageShuo);
if (codexVoice.Id != WindowsVoiceInputCatalog.CodexDictationId ||
    typelessVoice.Id != WindowsVoiceInputCatalog.TypelessId ||
    weChatVoice.Id != WindowsVoiceInputCatalog.WeChatVoiceId ||
    bageShuoVoice.Id != WindowsVoiceInputCatalog.BageShuoId ||
    weChatVoice.InputMethodProfile is not null ||
    bageShuoVoice.InputMethodProfile is not null)
{
    throw new InvalidOperationException(
        "App-based voice inputs must use their global dictation shortcuts instead of TSF profiles");
}
var typelessHold = WindowsVoiceInputShortcutCatalog.Resolve(typelessVoice.Kind);
var weChatHold = WindowsVoiceInputShortcutCatalog.Resolve(weChatVoice.Kind);
var bageShuoHold = WindowsVoiceInputShortcutCatalog.Resolve(bageShuoVoice.Kind);
var codexDictation = WindowsVoiceInputShortcutCatalog.Resolve(codexVoice.Kind);
if (!codexDictation.Keys.SequenceEqual([(ushort)0x11, (ushort)0x10, (ushort)0x44]) ||
    !typelessHold.Keys.SequenceEqual([(ushort)0xA4]) ||
    !weChatHold.Keys.SequenceEqual([(ushort)0xA2, (ushort)0x5B]) ||
    !bageShuoHold.Keys.SequenceEqual([(ushort)0xA5]))
{
    throw new InvalidOperationException(
        "Each selectable voice input must use its installed global dictation shortcut");
}

var codexStart = WindowsVoiceInputActivationPlan.Create(
    WindowsVoiceInputKind.CodexDictation,
    starting: true);
var codexStop = WindowsVoiceInputActivationPlan.Create(
    WindowsVoiceInputKind.CodexDictation,
    starting: false);
if (!codexStart.SequenceEqual([
        new WindowsVoiceInputKeyTransition(0x11, KeyUp: false),
        new WindowsVoiceInputKeyTransition(0x10, KeyUp: false),
        new WindowsVoiceInputKeyTransition(0x44, KeyUp: false)]) ||
    !codexStop.SequenceEqual([
        new WindowsVoiceInputKeyTransition(0x44, KeyUp: true),
        new WindowsVoiceInputKeyTransition(0x10, KeyUp: true),
        new WindowsVoiceInputKeyTransition(0x11, KeyUp: true)]))
{
    throw new InvalidOperationException(
        "RC003MS must hold Codex composer dictation for the real microphone session and release it afterward");
}

var typelessStart = WindowsVoiceInputActivationPlan.Create(
    WindowsVoiceInputKind.Typeless,
    starting: true);
var typelessStop = WindowsVoiceInputActivationPlan.Create(
    WindowsVoiceInputKind.Typeless,
    starting: false);
if (!typelessStart.SequenceEqual([
        new WindowsVoiceInputKeyTransition(0xA4, KeyUp: false),
        new WindowsVoiceInputKeyTransition(0xA4, KeyUp: true)]) ||
    !typelessStop.SequenceEqual([
        new WindowsVoiceInputKeyTransition(0xA4, KeyUp: false),
        new WindowsVoiceInputKeyTransition(0xA4, KeyUp: true)]) ||
    !WindowsVoiceInputInteractionPolicy.IsTapToToggle(WindowsVoiceInputKind.Typeless) ||
    WindowsVoiceInputInteractionPolicy.IsTapToToggle(WindowsVoiceInputKind.CodexDictation) ||
    !WindowsVoiceInputInteractionPolicy.GetHint(WindowsVoiceInputKind.Typeless)
        .Contains("轻触一次开始", StringComparison.Ordinal) ||
    !WindowsVoiceInputInteractionPolicy.GetRecordingHint(WindowsVoiceInputKind.Typeless)
        .Contains("已轻触 Left Alt 开始", StringComparison.Ordinal) ||
    !WindowsVoiceInputInteractionPolicy.GetStoppedHint(WindowsVoiceInputKind.BageShuo)
        .Contains("已轻触 Right Alt 结束", StringComparison.Ordinal))
{
    throw new InvalidOperationException(
        "Typeless must receive a complete Left Alt tap on press and another tap on release");
}
if (WindowsVoiceInputDispatchPolicy.BageShuoStartupTimeout < TimeSpan.FromSeconds(2) ||
    WindowsVoiceInputDispatchPolicy.BageShuoStartupTimeout > TimeSpan.FromSeconds(5))
{
    throw new InvalidOperationException(
        "BageShuo must get a bounded startup window before VoiceAnything sends Right Alt");
}

var weChatStart = WindowsVoiceInputActivationPlan.Create(
    WindowsVoiceInputKind.WeChatVoice,
    starting: true);
var weChatStop = WindowsVoiceInputActivationPlan.Create(
    WindowsVoiceInputKind.WeChatVoice,
    starting: false);
if (!weChatStart.SequenceEqual([
        new WindowsVoiceInputKeyTransition(0xA2, KeyUp: false),
        new WindowsVoiceInputKeyTransition(0x5B, KeyUp: false)]) ||
    !weChatStop.SequenceEqual([
        new WindowsVoiceInputKeyTransition(0x5B, KeyUp: true),
        new WindowsVoiceInputKeyTransition(0xA2, KeyUp: true)]))
{
    throw new InvalidOperationException(
        "WeChat voice input must retain its press-to-start/release-to-stop chord");
}

var bageShuoStart = WindowsVoiceInputActivationPlan.Create(
    WindowsVoiceInputKind.BageShuo,
    starting: true);
var bageShuoStop = WindowsVoiceInputActivationPlan.Create(
    WindowsVoiceInputKind.BageShuo,
    starting: false);
if (!bageShuoStart.SequenceEqual([
        new WindowsVoiceInputKeyTransition(0xA5, KeyUp: false),
        new WindowsVoiceInputKeyTransition(0xA5, KeyUp: true)]) ||
    !bageShuoStop.SequenceEqual([
        new WindowsVoiceInputKeyTransition(0xA5, KeyUp: false),
        new WindowsVoiceInputKeyTransition(0xA5, KeyUp: true)]))
{
    throw new InvalidOperationException(
        "BageShuo must receive a complete Right Alt tap on press and another tap on release");
}

Console.WriteLine("PASS selected voice input uses the correct toggle or hold shortcut lifecycle");

var inputMethodsCodePath = FindRepositoryFile(
    "Windows", "src", "SayAll.Windows", "WindowsInputMethods.cs");
var inputMethodsCode = await File.ReadAllTextAsync(inputMethodsCodePath);
if (inputMethodsCode.Contains("EnsureTypelessIsRunning", StringComparison.Ordinal) ||
    inputMethodsCode.Contains(
        "Process.Start(new ProcessStartInfo(executablePath)",
        StringComparison.Ordinal) &&
    inputMethodsCode.IndexOf("EnsureWeChatIsRunning", StringComparison.Ordinal) < 0)
{
    throw new InvalidOperationException(
        "The Typeless recording action must only trigger transcription and must never launch the Typeless app");
}

if (!inputMethodsCode.Contains("EnsureBageShuoIsRunning", StringComparison.Ordinal) ||
    !inputMethodsCode.Contains("本机未安装网易叭哥说", StringComparison.Ordinal) ||
    !inputMethodsCode.Contains("hotkey-listener", StringComparison.Ordinal) ||
    inputMethodsCode.Contains(
        "if (File.Exists(GetBageShuoExecutablePath()))\r\n        {\r\n            profiles.Add",
        StringComparison.Ordinal))
{
    throw new InvalidOperationException(
        "BageShuo must always remain manually selectable and return a visible error if its app is unavailable");
}

Console.WriteLine("PASS Typeless recording never launches or opens the Typeless application");

var nativeInputType = typeof(WindowsVoiceInputActivator).GetNestedType(
    "KeyboardInputEnvelope",
    System.Reflection.BindingFlags.NonPublic)
    ?? throw new InvalidOperationException("Voice-input native INPUT layout is missing");
if (Environment.Is64BitProcess && Marshal.SizeOf(nativeInputType) != 40)
{
    throw new InvalidOperationException(
        $"The Typeless SendInput envelope must be 40 bytes on x64, got {Marshal.SizeOf(nativeInputType)}");
}

Console.WriteLine("PASS Typeless uses the native x64 INPUT size accepted by Windows SendInput");

const string cableEndpointId =
    "{0.0.1.00000000}.{b2bb77b3-2134-450f-a364-b1039438dc74}";
const string cableWinRtId =
    @"\\?\SWD#MMDEVAPI#{0.0.1.00000000}.{b2bb77b3-2134-450f-a364-b1039438dc74}#{2eef81be-33fa-4800-9670-1cd474972c3f}";
if (!string.Equals(
        AudioCaptureEndpointId.Normalize(cableWinRtId),
        cableEndpointId,
        StringComparison.OrdinalIgnoreCase) ||
    !string.Equals(
        AudioCaptureEndpointId.Normalize(cableEndpointId),
        cableEndpointId,
        StringComparison.OrdinalIgnoreCase))
{
    throw new InvalidOperationException(
        "PolicyConfig must receive the MMDevice endpoint id rather than the WinRT DeviceInformation path");
}

Console.WriteLine("PASS WinRT CABLE Output paths normalize to PolicyConfig endpoint ids");

var captureRoutePlan = AudioCaptureRoutePlan.Create(
    previousDefaultEndpointId: "microphone",
    previousCommunicationsEndpointId: "headset",
    cableOutputEndpointId: "cable-output");
if (!captureRoutePlan.Activate.Select(change => change.EndpointId)
        .SequenceEqual(["cable-output", "cable-output", "cable-output"]) ||
    !captureRoutePlan.Restore.Select(change => change.EndpointId)
        .SequenceEqual(["microphone", "microphone", "headset"]))
{
    throw new InvalidOperationException(
        "Remote PCM must temporarily become the default capture source and restore both prior roles after dictation");
}

Console.WriteLine("PASS voice dictation routes CABLE Output temporarily and restores prior microphones");

if (!VoiceRuntimeStartupPolicy.ShouldConnect(
        bluetoothConnected: true,
        preferredInputMethodId: typelessVoice.Id,
        isConnected: false,
        isConnecting: false) ||
    VoiceRuntimeStartupPolicy.ShouldConnect(
        bluetoothConnected: false,
        preferredInputMethodId: typelessVoice.Id,
        isConnected: false,
        isConnecting: false) ||
    VoiceRuntimeStartupPolicy.ShouldConnect(
        bluetoothConnected: true,
        preferredInputMethodId: null,
        isConnected: false,
        isConnecting: false))
{
    throw new InvalidOperationException(
        "A saved voice input must keep the RC003MS ATVV audio path connected whenever Bluetooth is online");
}

Console.WriteLine("PASS saved voice input keeps the RC003MS voice runtime connected");

var mainWindowPath = FindRepositoryFile(
    "Windows", "src", "SayAll.Windows", "MainWindow.xaml");
var mainWindowDocument = XDocument.Load(mainWindowPath);
XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
var expectedStepButtons = new Dictionary<string, string>
{
    ["RemoteStepButton"] = "Remote",
    ["AudioStepButton"] = "Audio",
    ["VoiceStepButton"] = "VoiceTest",
    ["ControlsStepButton"] = "Controls",
};
foreach (var expected in expectedStepButtons)
{
    var button = mainWindowDocument
        .Descendants(presentation + "Button")
        .SingleOrDefault(element => (string?)element.Attribute(xaml + "Name") == expected.Key);
    if (button is null ||
        (string?)button.Attribute("Tag") != expected.Value ||
        (string?)button.Attribute("Click") != "OnboardingStep_Click")
    {
        throw new InvalidOperationException(
            $"The onboarding header must expose clickable {expected.Key} navigation");
    }
}
var finishButton = mainWindowDocument
    .Descendants(presentation + "Button")
    .Single(element => (string?)element.Attribute(xaml + "Name") == "FinishButton");
if ((string?)finishButton.Attribute("IsEnabled") == "False")
{
    throw new InvalidOperationException(
        "完成配置 must stay available even before optional three-button verification");
}

Console.WriteLine("PASS onboarding stages are clickable and button bindings can be saved");

var voiceInputTestBox = mainWindowDocument
    .Descendants(presentation + "TextBox")
    .SingleOrDefault(element =>
        (string?)element.Attribute(xaml + "Name") == "VoiceInputTestTextBox");
var voiceInputTestButton = mainWindowDocument
    .Descendants(presentation + "Button")
    .SingleOrDefault(element =>
        (string?)element.Attribute(xaml + "Name") == "VoiceInputTestButton");
var inputMethodStatus = mainWindowDocument
    .Descendants(presentation + "TextBlock")
    .SingleOrDefault(element =>
        (string?)element.Attribute(xaml + "Name") == "InputMethodStatus");
var inputMethodListBox = mainWindowDocument
    .Descendants(presentation + "ListBox")
    .SingleOrDefault(element =>
        (string?)element.Attribute(xaml + "Name") == "InputMethodListBox");
var inputMethodCardElements = mainWindowDocument.Descendants().ToArray();
if (voiceInputTestBox is null ||
    voiceInputTestButton is null ||
    inputMethodStatus is null ||
    inputMethodListBox is null ||
    (string?)inputMethodStatus.Attribute("TextWrapping") != "Wrap" ||
    Array.IndexOf(inputMethodCardElements, inputMethodStatus) >
        Array.IndexOf(inputMethodCardElements, inputMethodListBox) ||
    (string?)voiceInputTestButton.Attribute("PreviewMouseLeftButtonDown") !=
        "VoiceInputTest_PreviewMouseLeftButtonDown" ||
    (string?)voiceInputTestButton.Attribute("PreviewMouseLeftButtonUp") !=
        "VoiceInputTest_PreviewMouseLeftButtonUp")
{
    throw new InvalidOperationException(
        "Each home-page voice input must expose a hold-to-test control and a transcription target");
}

Console.WriteLine("PASS home page exposes hold-to-test voice input controls");

var liveSpectrumType = typeof(MainWindow).Assembly.GetType(
    "SayAll.Windows.LiveVoiceSpectrum");
if (liveSpectrumType is null)
{
    throw new InvalidOperationException(
        "The home page must expose a real-time LiveVoiceSpectrum control");
}
XNamespace local = "clr-namespace:SayAll.Windows";
var homeSpectrum = mainWindowDocument
    .Descendants(local + "LiveVoiceSpectrum")
    .SingleOrDefault(element =>
        (string?)element.Attribute(xaml + "Name") == "HomeVoiceSpectrum");
if (homeSpectrum is null ||
    !double.TryParse((string?)homeSpectrum.Attribute("Height"), out var spectrumHeight) ||
    spectrumHeight < 120)
{
    throw new InvalidOperationException(
        "The home connection page must reserve a visible high-detail live spectrum surface");
}
var expectedPaletteSelectors = new Dictionary<string, string>
{
    ["SpectrumPaletteBlue"] = "Blue",
    ["SpectrumPaletteEmber"] = "Ember",
    ["SpectrumPaletteEmerald"] = "Emerald",
};
foreach (var expectedSelector in expectedPaletteSelectors)
{
    var selector = mainWindowDocument
        .Descendants(presentation + "RadioButton")
        .SingleOrDefault(element =>
            (string?)element.Attribute(xaml + "Name") == expectedSelector.Key);
    if (selector is null ||
        (string?)selector.Attribute("Tag") != expectedSelector.Value ||
        (string?)selector.Attribute("Checked") != "SpectrumPalette_Checked")
    {
        throw new InvalidOperationException(
            $"The spectrum card must expose the {expectedSelector.Value} palette selector");
    }
}
var paletteSelectorStyle = mainWindowDocument
    .Descendants(presentation + "Style")
    .SingleOrDefault(element =>
        (string?)element.Attribute(xaml + "Key") == "SpectrumPaletteSelectorStyle");
var paletteBar = paletteSelectorStyle?
    .Descendants(presentation + "Border")
    .SingleOrDefault(element =>
        (string?)element.Attribute(xaml + "Name") == "PaletteBar");
var selectedBarExpands = paletteSelectorStyle?
    .Descendants(presentation + "Setter")
    .Any(element =>
        (string?)element.Attribute("TargetName") == "PaletteBar" &&
        (string?)element.Attribute("Property") == "Width" &&
        (string?)element.Attribute("Value") == "22") == true;
if (paletteSelectorStyle is null ||
    paletteBar is null ||
    paletteSelectorStyle.Descendants(presentation + "Ellipse").Any() ||
    !selectedBarExpands ||
    mainWindowDocument.Descendants(presentation + "TextBlock").Any(element =>
        (string?)element.Attribute("Text") == "COLOR"))
{
    throw new InvalidOperationException(
        "The spectrum palette selector must use restrained instrument ticks instead of candy-like color dots");
}
var mainWindowCodePath = FindRepositoryFile(
    "Windows", "src", "SayAll.Windows", "MainWindow.xaml.cs");
var mainWindowCode = await File.ReadAllTextAsync(mainWindowCodePath);
if (mainWindowCode.Contains("音量减键可直接切换推理强度", StringComparison.Ordinal))
{
    throw new InvalidOperationException(
        "The redesigned navigation layer must not display the obsolete Volume Down reasoning hint");
}
if (!mainWindowCode.Contains("VoiceSpectrumAnalyzer.Analyze(samples)", StringComparison.Ordinal) ||
    !mainWindowCode.Contains("HomeVoiceSpectrum.PushFrames", StringComparison.Ordinal) ||
    !mainWindowCode.Contains("HomeVoiceSpectrum.SetRecording(true)", StringComparison.Ordinal) ||
    !mainWindowCode.Contains("HomeVoiceSpectrum.SetRecording(false)", StringComparison.Ordinal) ||
    !mainWindowCode.Contains("await EnsureVoiceRuntimeAsync(state);", StringComparison.Ordinal))
{
    throw new InvalidOperationException(
        "The live spectrum must auto-connect saved RC003MS audio, consume real PCM, and freeze when recording ends");
}
if (!mainWindowCode.Contains("HomeVoiceSpectrum.SetPalette(_settings.SpectrumPalette)", StringComparison.Ordinal) ||
    !mainWindowCode.Contains("_settings.SpectrumPalette = selectedPalette", StringComparison.Ordinal) ||
    !mainWindowCode.Contains("HomeVoiceSpectrum.SetPalette(selectedPalette)", StringComparison.Ordinal))
{
    throw new InvalidOperationException(
        "Spectrum palette selection must redraw immediately and load from saved settings");
}

Console.WriteLine("PASS home page live spectrum consumes real PCM and freezes after recording");

var pcmFrameStart = mainWindowCode.IndexOf(
    "private void AtvvClient_PcmFrameReady",
    StringComparison.Ordinal);
var pcmFrameEnd = mainWindowCode.IndexOf(
    "private void AtvvClient_VoiceStopped",
    StringComparison.Ordinal);
var pcmFrameBody = mainWindowCode[pcmFrameStart..pcmFrameEnd];
if (!mainWindowCode.Contains("_spectrumTimer", StringComparison.Ordinal) ||
    !mainWindowCode.Contains("ConcurrentQueue<VoiceSpectrumFrame>", StringComparison.Ordinal) ||
    !mainWindowCode.Contains("MaxPendingSpectrumFrames", StringComparison.Ordinal) ||
    !pcmFrameBody.Contains("_pendingSpectrumFrames.Enqueue", StringComparison.Ordinal) ||
    pcmFrameBody.Contains("Dispatcher.BeginInvoke", StringComparison.Ordinal))
{
    throw new InvalidOperationException(
        "High-rate RC003MS PCM must be bounded and batch-rendered instead of dropping the visible history or starving WPF");
}

Console.WriteLine("PASS live spectrum batch-renders bounded RC003MS history without starving WPF");

var voiceStartedStart = mainWindowCode.IndexOf(
    "private void AtvvClient_VoiceStarted",
    StringComparison.Ordinal);
var renderSpectrumStart = mainWindowCode.IndexOf(
    "private void RenderLatestSpectrumFrame",
    StringComparison.Ordinal);
var renderSpectrumEnd = mainWindowCode.IndexOf(
    "private void StartBridgeLogWatcher",
    StringComparison.Ordinal);
var voiceStartedEnd = mainWindowCode.IndexOf(
    "private void AtvvClient_PcmFrameReady",
    StringComparison.Ordinal);
if (voiceStartedStart < 0 || voiceStartedEnd <= voiceStartedStart ||
    renderSpectrumStart < 0 || renderSpectrumEnd <= renderSpectrumStart)
{
    throw new InvalidOperationException(
        "The live spectrum session lifecycle must remain explicit and testable");
}
var voiceStartedBody = mainWindowCode[voiceStartedStart..voiceStartedEnd];
var renderSpectrumBody = mainWindowCode[renderSpectrumStart..renderSpectrumEnd];
if (!voiceStartedBody.Contains(
        "EnsureVoiceInputSessionStartedFromStream();",
        StringComparison.Ordinal))
{
    throw new InvalidOperationException(
        "A real RC003MS audio stream must start the selected Codex input even when firmware omits the microphone-request event");
}
if (voiceStartedBody.Contains("HomeVoiceSpectrum.SetRecording(true)", StringComparison.Ordinal) ||
    !renderSpectrumBody.Contains("HomeVoiceSpectrum.SetRecording(true)", StringComparison.Ordinal) ||
    !mainWindowCode.Contains("_spectrumHasRenderedCurrentSession", StringComparison.Ordinal))
{
    throw new InvalidOperationException(
        "A short RC003MS press without PCM must preserve the last valid spectrum until the first real frame arrives");
}

Console.WriteLine("PASS empty RC003MS presses preserve the last valid spectrum");

var voiceSessionStart = mainWindowCode.IndexOf(
    "private string? StartVoiceInputSession",
    StringComparison.Ordinal);
var voiceSessionStop = mainWindowCode.IndexOf(
    "private void StopVoiceInputSession",
    StringComparison.Ordinal);
if (voiceSessionStart < 0 || voiceSessionStop <= voiceSessionStart)
{
    throw new InvalidOperationException(
        "The local voice-input session lifecycle must remain explicit and testable");
}
var voiceSessionBody = mainWindowCode[voiceSessionStart..voiceSessionStop];
if (
    voiceSessionBody.IndexOf("DefaultAudioCaptureRoute.Open(captureDeviceId)", StringComparison.Ordinal) >
    voiceSessionBody.IndexOf("WindowsVoiceInputActivator.Start(profile)", StringComparison.Ordinal))
{
    throw new InvalidOperationException(
        "CABLE Output must become the default microphone before Typeless starts capturing");
}
var microphoneHandlerStart = mainWindowCode.IndexOf(
    "private void HandleRemoteMicrophonePressed",
    StringComparison.Ordinal);
var microphoneHandlerEnd = mainWindowCode.IndexOf(
    "private void AtvvClient_VoiceStarted",
    StringComparison.Ordinal);
var microphoneHandlerBody = mainWindowCode[microphoneHandlerStart..microphoneHandlerEnd];
if (microphoneHandlerBody.IndexOf("StopVoiceInputSession();", StringComparison.Ordinal) >
    microphoneHandlerBody.IndexOf("RequestMicrophoneCloseAsync", StringComparison.Ordinal))
{
    throw new InvalidOperationException(
        "The second microphone press must tap Left Alt immediately before optional remote-audio shutdown");
}
var remoteVoiceStoppedStart = mainWindowCode.IndexOf(
    "private void AtvvClient_VoiceStopped",
    StringComparison.Ordinal);
var remoteVoiceStoppedEnd = mainWindowCode.IndexOf(
    "private void AtvvClient_Failed",
    StringComparison.Ordinal);
var remoteVoiceStoppedBody = mainWindowCode[remoteVoiceStoppedStart..remoteVoiceStoppedEnd];
var remoteMicrophonePressedStart = mainWindowCode.IndexOf(
    "private void AtvvClient_RemoteMicrophonePressed",
    StringComparison.Ordinal);
var remoteMicrophonePressedEnd = mainWindowCode.IndexOf(
    "private void HandleRemoteMicrophonePressed",
    StringComparison.Ordinal);
var remoteMicrophonePressedBody = mainWindowCode[
    remoteMicrophonePressedStart..remoteMicrophonePressedEnd];
if (!remoteVoiceStoppedBody.Contains("StopVoiceInputSession();", StringComparison.Ordinal) ||
    !remoteVoiceStoppedBody.Contains("_voiceToggle.NotifyVoiceStopped();", StringComparison.Ordinal) ||
    remoteVoiceStoppedBody.IndexOf("if (IsMicrophoneSessionHeld)", StringComparison.Ordinal) < 0 ||
    remoteVoiceStoppedBody.IndexOf("if (IsMicrophoneSessionHeld)", StringComparison.Ordinal) >
        remoteVoiceStoppedBody.IndexOf("StopVoiceInputSession();", StringComparison.Ordinal) ||
    !remoteVoiceStoppedBody.Contains("await _atvvClient.RequestMicrophoneOpenAsync();", StringComparison.Ordinal) ||
    !remoteMicrophonePressedBody.Contains("if (_hookReady)", StringComparison.Ordinal) ||
    !mainWindowCode.Contains("HandleRemoteMicrophoneReleased(timestamp);", StringComparison.Ordinal) ||
    !voiceStartedBody.Contains("continuingHeldSession", StringComparison.Ordinal) ||
    !voiceStartedBody.Contains("_hookReady && !IsMicrophoneSessionHeld", StringComparison.Ordinal) ||
    !mainWindowCode.Contains(
        "_microphoneHidPressed || _voiceInputTestPressed",
        StringComparison.Ordinal))
{
    throw new InvalidOperationException(
        "ATVV segment stops must not release Codex while HID is held; the physical HID release must end dictation");
}

Console.WriteLine("PASS RC003MS physical HID release exclusively ends hold-to-talk dictation");

var atvvClientCodePath = FindRepositoryFile(
    "Windows", "src", "SayAll.Windows", "Rc003AtvvClient.cs");
var atvvClientCode = await File.ReadAllTextAsync(atvvClientCodePath);
if (!atvvClientCode.Contains("AtvvProtocol.MicrophoneExtend", StringComparison.Ordinal) ||
    !atvvClientCode.Contains("RunMicrophoneKeepAliveAsync", StringComparison.Ordinal) ||
    atvvClientCode.Contains("ScheduleMicrophoneReopen", StringComparison.Ordinal) ||
    atvvClientCode.Contains("MicrophoneReopenAttemptCount", StringComparison.Ordinal))
{
    throw new InvalidOperationException(
        "A hold-to-talk RC003MS session must keep active audio alive without reopening after release");
}
var requestOpenStart = atvvClientCode.IndexOf(
    "public async Task RequestMicrophoneOpenAsync",
    StringComparison.Ordinal);
var requestCloseStart = atvvClientCode.IndexOf(
    "public async Task RequestMicrophoneCloseAsync",
    StringComparison.Ordinal);
var requestOpenBody = atvvClientCode[requestOpenStart..requestCloseStart];
if (requestOpenBody.Contains("ScheduleMicrophoneReopen();", StringComparison.Ordinal))
{
    throw new InvalidOperationException(
        "The physical hold-to-talk stream must not be reopened after the user releases the microphone key");
}
var disposeStart = atvvClientCode.IndexOf(
    "public async ValueTask DisposeAsync",
    StringComparison.Ordinal);
var requestCloseBody = atvvClientCode[requestCloseStart..disposeStart];
if (requestCloseBody.IndexOf("StopMicrophoneSession();", StringComparison.Ordinal) >
    requestCloseBody.IndexOf("AtvvProtocol.MicrophoneClose", StringComparison.Ordinal))
{
    throw new InvalidOperationException(
        "The second microphone press must cancel keepalive and pending reopen before sending MIC_CLOSE");
}

Console.WriteLine("PASS RC003MS audio follows the physical microphone-key hold lifecycle");

var remoteAssetPath = FindRepositoryFile(
    "Resources", "RC003MS-remote-front-v2.png");
var remoteAssetBytes = await File.ReadAllBytesAsync(remoteAssetPath);
if (remoteAssetBytes.Length < 26 || remoteAssetBytes[25] != 6)
{
    throw new InvalidOperationException(
        "The RC003MS selector asset must be a genuine RGBA PNG, not a baked background");
}
var remoteAssetWidth = BinaryPrimitives.ReadUInt32BigEndian(remoteAssetBytes.AsSpan(16, 4));
var remoteAssetHeight = BinaryPrimitives.ReadUInt32BigEndian(remoteAssetBytes.AsSpan(20, 4));
var remoteAssetAspect = (double)remoteAssetWidth / remoteAssetHeight;
if (remoteAssetAspect is < 0.45 or > 0.55)
{
    throw new InvalidOperationException(
        $"The RC003MS front selector must keep its narrow real proportions, got {remoteAssetWidth}x{remoteAssetHeight}");
}
var remoteImages = mainWindowDocument
    .Descendants(presentation + "Image")
    .Where(element =>
        ((string?)element.Attribute("Source"))?.EndsWith(
            "/Resources/RC003MS-remote-front-v2.png",
            StringComparison.Ordinal) == true)
    .ToArray();
if (remoteImages.Length != 2)
{
    throw new InvalidOperationException(
        "Both the home connection page and button editor must render the verified RC003MS front asset");
}

Console.WriteLine("PASS home page and button editor use the verified transparent RC003MS front render");

static string FindRepositoryFile(params string[] segments)
{
    foreach (var start in new[]
             {
                 Directory.GetCurrentDirectory(),
                 AppContext.BaseDirectory,
             })
    {
        var directory = new DirectoryInfo(start);
        while (directory is not null)
        {
            var candidate = Path.Combine([directory.FullName, .. segments]);
            if (File.Exists(candidate))
            {
                return candidate;
            }
            directory = directory.Parent;
        }
    }

    throw new FileNotFoundException(
        $"Could not locate repository file: {Path.Combine(segments)}");
}
