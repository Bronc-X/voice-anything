using SayAll.Core.Input;

namespace SayAll.Windows;

public sealed record RemoteActionShortcut(
    ushort Key,
    IReadOnlyList<ushort> Modifiers);

public static class RemoteActionShortcutCatalog
{
    public static RemoteActionShortcut? ResolveForProcess(
        RemoteButtonAction action,
        string processName)
    {
        return RemoteActionTargetCatalog.IsCodexProcess(processName)
            ? Resolve(action)
            : null;
    }

    public static RemoteActionShortcut? Resolve(RemoteButtonAction action)
    {
        return action switch
        {
            RemoteButtonAction.GoBack => new(0xDB, [0x11]),
            RemoteButtonAction.NewChat => new(0x4E, [0x11]),
            RemoteButtonAction.NewStandaloneChat => new(0x4F, [0x11, 0x12]),
            RemoteButtonAction.PreviousChat => new(0x21, [0x11]),
            RemoteButtonAction.NextChat => new(0x22, [0x11]),
            RemoteButtonAction.NextChatNeedingAttention => new(0x41, [0x11, 0x12]),
            RemoteButtonAction.SubmitTask => new(0x0D, [0x11]),
            RemoteButtonAction.SelectModel => new(0x4D, [0x11, 0x10]),
            RemoteButtonAction.SelectReasoning => new(0x7B, [0x11, 0x12, 0x10]),
            RemoteButtonAction.OpenCommandMenu => new(0x27, [0x11, 0x12]),
            RemoteButtonAction.OpenProjectPicker => new(0x4F, [0x11, 0x12, 0x10]),
            RemoteButtonAction.ToggleSidebar => new(0x42, [0x11]),
            RemoteButtonAction.ToggleTerminal => new(0xC0, [0x11]),
            RemoteButtonAction.SearchFiles => new(0x50, [0x11]),
            RemoteButtonAction.ToggleFileTree => new(0x45, [0x11, 0x10]),
            RemoteButtonAction.OpenReview => new(0x47, [0x11, 0x10]),
            RemoteButtonAction.ToggleReviewPanel => new(0x42, [0x11, 0x12]),
            RemoteButtonAction.OpenBrowser => new(0x54, [0x11]),
            RemoteButtonAction.OpenSettings => new(0xBC, [0x11]),
            RemoteButtonAction.ApproveRequest => new(0x0D, []),
            RemoteButtonAction.DeclineRequest => new(0x1B, []),
            _ => null,
        };
    }
}
