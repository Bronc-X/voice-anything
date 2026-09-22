using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using SayAll.Core.Input;

namespace SayAll.Windows;

public sealed record RemoteActionExecutionResult(bool Executed, string Message);

public static class CodexRunningItemSwitchPlan
{
    private const ushort VkPageUp = 0x21;
    private const ushort VkPageDown = 0x22;
    private const ushort VkControl = 0x11;

    public static RemoteActionShortcut Create(bool previous)
    {
        return new RemoteActionShortcut(
            previous ? VkPageUp : VkPageDown,
            [VkControl]);
    }
}

public static class CodexSidebarScrollPlan
{
    public static int Resolve(RemoteButtonAction action)
    {
        return 0;
    }
}

public sealed class RemoteActionExecutor
{
    private const uint InputMouse = 0;
    private const uint InputKeyboard = 1;
    private const uint KeyEventKeyUp = 0x0002;
    private const uint KeyEventUnicode = 0x0004;
    private const uint MouseEventWheel = 0x0800;
    private const ushort VkShift = 0x10;
    private const ushort VkControl = 0x11;
    private const ushort VkEscape = 0x1B;
    private const ushort VkReturn = 0x0D;
    private const ushort VkUp = 0x26;
    private const ushort VkRight = 0x27;
    private const ushort VkDown = 0x28;
    private const ushort VkF = 0x46;
    private const ushort VkL = 0x4C;
    private const ushort VkY = 0x59;
    private const ushort VkZ = 0x5A;
    private const ushort VkVolumeUp = 0xAF;
    private const ushort VkVolumeDown = 0xAE;
    private readonly RemoteNavigationState _navigationState =
        new(TimeSpan.FromSeconds(10));
    private readonly ProjectToolMenuState _projectTools = new();
    private ProjectToolsOverlayWindow? _projectToolsOverlay;

    public RemoteActionExecutionResult Execute(
        RemoteButtonBinding binding,
        string activePreset)
    {
        ArgumentNullException.ThrowIfNull(binding);
        if (binding.Action == RemoteButtonAction.Disabled)
        {
            return new RemoteActionExecutionResult(false, "该按键已设为不执行");
        }

        if (!TryGetSupportedForegroundProcess(out var processName, out var foregroundWindow))
        {
            return new RemoteActionExecutionResult(
                false,
                "已识别按键；切换到 Codex、Claude Code 或其终端后才会执行");
        }

        if (binding.Action == RemoteButtonAction.OpenProjectTools)
        {
            if (_projectTools.IsOpen)
            {
                CloseProjectTools();
                return new RemoteActionExecutionResult(true, "已关闭项目工具");
            }

            _navigationState.LeaveOperation();
            _projectTools.Open();
            ShowProjectTools();
            return new RemoteActionExecutionResult(true, "项目工具已打开；方向键选择，OK 执行");
        }

        if (_projectTools.IsOpen)
        {
            return ExecuteProjectToolInput(binding, activePreset);
        }

        var navigationDecision = _navigationState.Resolve(
            binding.Action,
            DateTimeOffset.Now);
        if (navigationDecision != RemoteNavigationDecision.None)
        {
            ExecuteNavigation(navigationDecision, foregroundWindow, processName);
            return new RemoteActionExecutionResult(
                true,
                navigationDecision switch
                {
                    RemoteNavigationDecision.ScrollSidebarUp or
                        RemoteNavigationDecision.ScrollSidebarDown =>
                        "已滚动最左侧对话列表",
                    RemoteNavigationDecision.ConfirmSelection =>
                        "已打开选中的运行项目",
                    RemoteNavigationDecision.DismissOperation =>
                        "已退出当前选择界面",
                    _ => "已在当前操作中移动选择",
                });
        }

        var sidebarScrollDelta = CodexSidebarScrollPlan.Resolve(binding.Action);
        if (sidebarScrollDelta != 0)
        {
            if (RemoteActionTargetCatalog.IsCodexProcess(processName))
            {
                ScrollCodexSidebar(foregroundWindow, sidebarScrollDelta);
            }
            else
            {
                SendWheel(sidebarScrollDelta);
            }

            return new RemoteActionExecutionResult(true, "已滚动最左侧对话列表");
        }

        if (binding.Action is RemoteButtonAction.PreviousProject or
            RemoteButtonAction.NextProject)
        {
            if (!RemoteActionTargetCatalog.IsCodexProcess(processName))
            {
                return new RemoteActionExecutionResult(
                    false,
                    "切换项目仅支持 Codex 桌面应用；请先切换到 Codex 窗口");
            }

            ExecuteProjectSwitch(binding.Action == RemoteButtonAction.PreviousProject);
            _navigationState.LeaveOperation();
            return new RemoteActionExecutionResult(
                true,
                binding.Action == RemoteButtonAction.PreviousProject
                    ? "已切换到上一个运行项目"
                    : "已切换到下一个运行项目");
        }

        var documentedShortcut = RemoteActionShortcutCatalog.Resolve(binding.Action);
        if (documentedShortcut is not null)
        {
            var shortcut = RemoteActionShortcutCatalog.ResolveForProcess(
                binding.Action,
                processName);
            if (shortcut is not null)
            {
                SendChord(shortcut.Key, shortcut.Modifiers.ToArray());
                if (binding.Action is RemoteButtonAction.SelectModel or
                    RemoteButtonAction.OpenProjectPicker or
                    RemoteButtonAction.OpenCommandMenu)
                {
                    _navigationState.EnterOperation(DateTimeOffset.Now);
                }
                else if (binding.Action is RemoteButtonAction.ApproveRequest or
                    RemoteButtonAction.DeclineRequest or
                    RemoteButtonAction.NewChat or
                    RemoteButtonAction.OpenBrowser)
                {
                    _navigationState.LeaveOperation();
                }

                return new RemoteActionExecutionResult(
                    true,
                    $"已在 {processName} 执行：{binding.Action}");
            }

            if (binding.Action == RemoteButtonAction.SelectModel &&
                activePreset == "Claude Code")
            {
                SendUnicodeText("/model ");
                return new RemoteActionExecutionResult(
                    true,
                    $"已在 {processName} 执行：{binding.Action}");
            }

            return new RemoteActionExecutionResult(
                false,
                "该动作仅支持 Codex 桌面应用；请先切换到 Codex 窗口");
        }

        switch (binding.Action)
        {
            case RemoteButtonAction.Escape:
                SendChord(VkEscape);
                _navigationState.LeaveOperation();
                break;
            case RemoteButtonAction.SubmitTask:
                SendChord(VkReturn);
                _navigationState.LeaveOperation();
                break;
            case RemoteButtonAction.NewLine:
                SendChord(VkReturn, VkShift);
                break;
            case RemoteButtonAction.ScrollUp:
                SendWheel(480);
                break;
            case RemoteButtonAction.ScrollDown:
                SendWheel(-480);
                break;
            case RemoteButtonAction.NavigatePrevious:
                SendChord(VkUp);
                break;
            case RemoteButtonAction.NavigateNext:
                SendChord(VkDown);
                break;
            case RemoteButtonAction.FocusPrompt:
                SendChord(VkL, VkControl);
                break;
            case RemoteButtonAction.Find:
                SendChord(VkF, VkControl);
                break;
            case RemoteButtonAction.Undo:
                SendChord(VkZ, VkControl);
                break;
            case RemoteButtonAction.Redo:
                SendChord(VkY, VkControl);
                break;
            case RemoteButtonAction.InsertSkill:
                SendUnicodeText(string.IsNullOrEmpty(binding.Argument) ? "$" : binding.Argument);
                break;
            case RemoteButtonAction.VolumeUp:
                SendChord(VkVolumeUp);
                break;
            case RemoteButtonAction.VolumeDown:
                SendChord(VkVolumeDown);
                break;
            case RemoteButtonAction.OpenCodex:
            case RemoteButtonAction.OpenClaudeCode:
                return new RemoteActionExecutionResult(
                    false,
                    "为避免启动任意程序，打开应用动作需要从安装器登记后才能使用");
            default:
                return new RemoteActionExecutionResult(false, "该动作尚未实现");
        }

        return new RemoteActionExecutionResult(
            true,
            $"已在 {processName} 执行：{binding.Action}");
    }

    private static void ExecuteProjectSwitch(bool previous)
    {
        var shortcut = CodexRunningItemSwitchPlan.Create(previous);
        SendChord(shortcut.Key, shortcut.Modifiers.ToArray());
    }

    private static void ExecuteNavigation(
        RemoteNavigationDecision decision,
        IntPtr foregroundWindow,
        string processName)
    {
        switch (decision)
        {
            case RemoteNavigationDecision.NavigateUp:
                SendChord(VkUp);
                return;
            case RemoteNavigationDecision.NavigateDown:
                SendChord(VkDown);
                return;
            case RemoteNavigationDecision.ConfirmSelection:
                SendChord(VkReturn);
                return;
            case RemoteNavigationDecision.DismissOperation:
                SendChord(VkEscape);
                return;
            case RemoteNavigationDecision.ScrollSidebarUp:
                if (RemoteActionTargetCatalog.IsCodexProcess(processName))
                {
                    ScrollCodexSidebar(foregroundWindow, 480);
                }
                else
                {
                    SendWheel(480);
                }
                return;
            case RemoteNavigationDecision.ScrollSidebarDown:
                if (RemoteActionTargetCatalog.IsCodexProcess(processName))
                {
                    ScrollCodexSidebar(foregroundWindow, -480);
                }
                else
                {
                    SendWheel(-480);
                }
                return;
            case RemoteNavigationDecision.ScrollProjectUp:
                if (RemoteActionTargetCatalog.IsCodexProcess(processName))
                {
                    ScrollCodexContent(foregroundWindow, 480);
                }
                else
                {
                    SendWheel(480);
                }
                return;
            case RemoteNavigationDecision.ScrollProjectDown:
                if (RemoteActionTargetCatalog.IsCodexProcess(processName))
                {
                    ScrollCodexContent(foregroundWindow, -480);
                }
                else
                {
                    SendWheel(-480);
                }
                return;
        }
    }

    private RemoteActionExecutionResult ExecuteProjectToolInput(
        RemoteButtonBinding binding,
        string activePreset)
    {
        var delta = binding.Action switch
        {
            RemoteButtonAction.PreviousChat => -3,
            RemoteButtonAction.NextChat => 3,
            RemoteButtonAction.PreviousProject or
                RemoteButtonAction.NavigatePrevious or
                RemoteButtonAction.SelectModel => -1,
            RemoteButtonAction.NextProject or
                RemoteButtonAction.NavigateNext or
                RemoteButtonAction.SelectReasoning => 1,
            _ => 0,
        };
        if (delta != 0)
        {
            _projectTools.Move(delta);
            ShowProjectTools();
            return new RemoteActionExecutionResult(
                true,
                $"项目工具：{_projectTools.SelectedItem.Title}");
        }

        if (binding.Action == RemoteButtonAction.SubmitTask)
        {
            var selected = _projectTools.Confirm();
            HideProjectTools();
            return selected is null
                ? new RemoteActionExecutionResult(false, "项目工具尚未打开")
                : Execute(selected, activePreset);
        }

        if (binding.Action is RemoteButtonAction.GoBack or RemoteButtonAction.Escape)
        {
            CloseProjectTools();
            return new RemoteActionExecutionResult(true, "已关闭项目工具");
        }

        return new RemoteActionExecutionResult(
            false,
            "项目工具已打开；请用方向键选择、OK 执行、返回键关闭");
    }

    private void ShowProjectTools()
    {
        _projectToolsOverlay ??= new ProjectToolsOverlayWindow();
        _projectToolsOverlay.ShowState(_projectTools);
    }

    private void HideProjectTools()
    {
        _projectToolsOverlay?.Hide();
    }

    private void CloseProjectTools()
    {
        _projectTools.Close();
        HideProjectTools();
    }

    private static void ScrollCodexSidebar(IntPtr window, int delta)
    {
        if (!GetWindowRect(window, out var bounds) ||
            !GetCursorPos(out var originalCursor))
        {
            SendWheel(delta);
            return;
        }

        var width = Math.Max(1, bounds.Right - bounds.Left);
        var height = Math.Max(1, bounds.Bottom - bounds.Top);
        var sidebarX = bounds.Left + Math.Min(180, Math.Max(80, width / 8));
        var sidebarY = bounds.Top + Math.Min(
            height - 80,
            Math.Max(120, height / 2));
        if (!SetCursorPos(sidebarX, sidebarY))
        {
            SendWheel(delta);
            return;
        }

        SendWheel(delta);
        Thread.Sleep(8);
        _ = SetCursorPos(originalCursor.X, originalCursor.Y);
    }

    private static void ScrollCodexContent(IntPtr window, int delta)
    {
        if (!GetWindowRect(window, out var bounds) ||
            !GetCursorPos(out var originalCursor))
        {
            SendWheel(delta);
            return;
        }

        var width = Math.Max(1, bounds.Right - bounds.Left);
        var height = Math.Max(1, bounds.Bottom - bounds.Top);
        var contentX = bounds.Left + Math.Clamp(width * 2 / 3, 0, width - 1);
        var contentY = bounds.Top + Math.Clamp(height / 2, 0, height - 1);
        if (!SetCursorPos(contentX, contentY))
        {
            SendWheel(delta);
            return;
        }

        SendWheel(delta);
        Thread.Sleep(8);
        _ = SetCursorPos(originalCursor.X, originalCursor.Y);
    }

    private static bool TryGetSupportedForegroundProcess(
        out string processName,
        out IntPtr window)
    {
        processName = string.Empty;
        window = GetForegroundWindow();
        if (window == IntPtr.Zero)
        {
            return false;
        }

        _ = GetWindowThreadProcessId(window, out var processId);
        if (processId == 0)
        {
            return false;
        }

        try
        {
            processName = Process.GetProcessById((int)processId).ProcessName;
            return RemoteActionTargetCatalog.SupportsProcess(processName);
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static void SendChord(ushort key, params ushort[] modifiers)
    {
        var inputs = new List<Input>();
        inputs.AddRange(modifiers.Select(modifier => CreateKeyboardInput(modifier, keyUp: false)));
        inputs.Add(CreateKeyboardInput(key, keyUp: false));
        inputs.Add(CreateKeyboardInput(key, keyUp: true));
        inputs.AddRange(modifiers.Reverse().Select(modifier => CreateKeyboardInput(modifier, keyUp: true)));
        Send(inputs);
    }

    private static void SendWheel(int delta)
    {
        Send([
            new Input
            {
                Type = InputMouse,
                Union = new InputUnion
                {
                    Mouse = new MouseInput
                    {
                        MouseData = unchecked((uint)delta),
                        Flags = MouseEventWheel,
                    },
                },
            },
        ]);
    }

    private static void SendUnicodeText(string text)
    {
        var inputs = new List<Input>(text.Length * 2);
        foreach (var character in text)
        {
            inputs.Add(UnicodeInput(character, keyUp: false));
            inputs.Add(UnicodeInput(character, keyUp: true));
        }

        Send(inputs);
    }

    private static Input CreateKeyboardInput(ushort key, bool keyUp)
    {
        return new Input
        {
            Type = InputKeyboard,
            Union = new InputUnion
            {
                Keyboard = new KeyboardInput
                {
                    VirtualKey = key,
                    Flags = keyUp ? KeyEventKeyUp : 0,
                },
            },
        };
    }

    private static Input UnicodeInput(char character, bool keyUp)
    {
        return new Input
        {
            Type = InputKeyboard,
            Union = new InputUnion
            {
                Keyboard = new KeyboardInput
                {
                    ScanCode = character,
                    Flags = KeyEventUnicode | (keyUp ? KeyEventKeyUp : 0),
                },
            },
        };
    }

    private static void Send(IReadOnlyCollection<Input> inputs)
    {
        var sent = SendInput(
            (uint)inputs.Count,
            inputs.ToArray(),
            Marshal.SizeOf<Input>());
        if (sent != inputs.Count)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows 未能发送遥控器动作");
        }
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr window, out Rect bounds);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out Point point);

    [DllImport("user32.dll")]
    private static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint inputCount, Input[] inputs, int inputSize);

    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public uint Type;
        public InputUnion Union;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)]
        public MouseInput Mouse;

        [FieldOffset(0)]
        public KeyboardInput Keyboard;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput
    {
        public int X;
        public int Y;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public UIntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInput
    {
        public ushort VirtualKey;
        public ushort ScanCode;
        public uint Flags;
        public uint Time;
        public UIntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }
}
