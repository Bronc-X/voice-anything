using SayAll.Core.Input;

namespace SayAll.Windows;

public enum RemoteNavigationDecision
{
    None,
    ScrollSidebarUp,
    ScrollSidebarDown,
    ScrollProjectUp,
    ScrollProjectDown,
    NavigateUp,
    NavigateDown,
    ConfirmSelection,
    DismissOperation,
}

public sealed class RemoteNavigationState
{
    private readonly TimeSpan _operationLifetime;
    private DateTimeOffset _operationExpiresAt = DateTimeOffset.MinValue;

    public RemoteNavigationState(TimeSpan operationLifetime)
    {
        if (operationLifetime <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(operationLifetime));
        }

        _operationLifetime = operationLifetime;
    }

    public void EnterOperation(DateTimeOffset now)
    {
        _operationExpiresAt = now + _operationLifetime;
    }

    public void LeaveOperation()
    {
        _operationExpiresAt = DateTimeOffset.MinValue;
    }

    public RemoteNavigationDecision Resolve(
        RemoteButtonAction action,
        DateTimeOffset now)
    {
        var operationIsActive = now < _operationExpiresAt;
        if (operationIsActive)
        {
            var operationDecision = action switch
            {
                RemoteButtonAction.ContextUp or
                    RemoteButtonAction.PreviousChat or
                    RemoteButtonAction.NavigatePrevious or
                    RemoteButtonAction.PreviousProject or
                    RemoteButtonAction.SelectModel =>
                    RemoteNavigationDecision.NavigateUp,
                RemoteButtonAction.ContextDown or
                    RemoteButtonAction.NextChat or
                    RemoteButtonAction.NavigateNext or
                    RemoteButtonAction.NextProject or
                    RemoteButtonAction.SelectReasoning =>
                    RemoteNavigationDecision.NavigateDown,
                RemoteButtonAction.SubmitTask =>
                    RemoteNavigationDecision.ConfirmSelection,
                RemoteButtonAction.GoBack or RemoteButtonAction.Escape =>
                    RemoteNavigationDecision.DismissOperation,
                _ => RemoteNavigationDecision.None,
            };

            if (operationDecision is RemoteNavigationDecision.NavigateUp or
                RemoteNavigationDecision.NavigateDown)
            {
                EnterOperation(now);
            }
            else if (operationDecision is RemoteNavigationDecision.ConfirmSelection or
                RemoteNavigationDecision.DismissOperation)
            {
                LeaveOperation();
            }

            return operationDecision;
        }

        return action switch
        {
            RemoteButtonAction.ContextUp or RemoteButtonAction.PreviousChat =>
                RemoteNavigationDecision.ScrollProjectUp,
            RemoteButtonAction.ContextDown or RemoteButtonAction.NextChat =>
                RemoteNavigationDecision.ScrollProjectDown,
            _ => RemoteNavigationDecision.None,
        };
    }
}

public sealed record ProjectToolItem(
    string Title,
    string Hint,
    RemoteButtonBinding Binding);

public sealed class ProjectToolMenuState
{
    private static readonly ProjectToolItem[] AvailableItems =
    [
        new("输入框", "聚焦任务输入", new(RemoteButtonAction.FocusPrompt)),
        new("终端", "显示或隐藏终端", new(RemoteButtonAction.ToggleTerminal)),
        new("文件树", "显示或隐藏文件树", new(RemoteButtonAction.ToggleFileTree)),
        new("搜索", "查找工作区文件", new(RemoteButtonAction.SearchFiles)),
        new("审查", "打开代码审查", new(RemoteButtonAction.OpenReview)),
        new("浏览器", "打开内置浏览器", new(RemoteButtonAction.OpenBrowser)),
        new("模型", "选择当前模型", new(RemoteButtonAction.SelectModel)),
        new("推理", "选择推理强度", new(RemoteButtonAction.SelectReasoning)),
        new("Skill", "插入 $ 并继续输入", new(RemoteButtonAction.InsertSkill, "$")),
    ];

    public IReadOnlyList<ProjectToolItem> Items => AvailableItems;

    public bool IsOpen { get; private set; }

    public int SelectedIndex { get; private set; }

    public ProjectToolItem SelectedItem => AvailableItems[SelectedIndex];

    public void Open()
    {
        SelectedIndex = 0;
        IsOpen = true;
    }

    public void Move(int delta)
    {
        if (!IsOpen || delta == 0)
        {
            return;
        }

        SelectedIndex = (SelectedIndex + delta) % AvailableItems.Length;
        if (SelectedIndex < 0)
        {
            SelectedIndex += AvailableItems.Length;
        }
    }

    public RemoteButtonBinding? Confirm()
    {
        if (!IsOpen)
        {
            return null;
        }

        IsOpen = false;
        return SelectedItem.Binding;
    }

    public void Close()
    {
        IsOpen = false;
    }
}

public static class CodexProjectSwitchPlan
{
    public static TimeSpan PickerOpenDelay { get; } = TimeSpan.FromMilliseconds(120);
    public static TimeSpan SelectionDelay { get; } = TimeSpan.FromMilliseconds(30);

    private const ushort VkO = 0x4F;
    private const ushort VkUp = 0x26;
    private const ushort VkDown = 0x28;
    private const ushort VkReturn = 0x0D;
    private const ushort VkShift = 0x10;
    private const ushort VkControl = 0x11;
    private const ushort VkAlt = 0x12;

    public static IReadOnlyList<RemoteActionShortcut> Create(bool previous)
    {
        return
        [
            new RemoteActionShortcut(VkO, [VkControl, VkAlt, VkShift]),
            new RemoteActionShortcut(previous ? VkUp : VkDown, []),
            new RemoteActionShortcut(VkReturn, []),
        ];
    }
}
