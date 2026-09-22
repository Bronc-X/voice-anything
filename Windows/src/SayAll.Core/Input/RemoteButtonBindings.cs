namespace SayAll.Core.Input;

public enum RemoteButtonTrigger
{
    SingleClick,
    DoubleClick,
    LongPress,
}

public enum RemoteButtonAction
{
    Disabled,
    Escape,
    SubmitTask,
    NewLine,
    ScrollUp,
    ScrollDown,
    ContextUp,
    ContextDown,
    NavigatePrevious,
    NavigateNext,
    PreviousProject,
    NextProject,
    GoBack,
    FocusPrompt,
    Find,
    Undo,
    Redo,
    SelectModel,
    SelectReasoning,
    InsertSkill,
    NewChat,
    NewStandaloneChat,
    PreviousChat,
    NextChat,
    NextChatNeedingAttention,
    OpenCommandMenu,
    OpenProjectPicker,
    OpenProjectTools,
    ToggleSidebar,
    ToggleTerminal,
    SearchFiles,
    ToggleFileTree,
    OpenReview,
    ToggleReviewPanel,
    OpenBrowser,
    OpenSettings,
    ApproveRequest,
    DeclineRequest,
    OpenCodex,
    OpenClaudeCode,
    VolumeUp,
    VolumeDown,
}

public sealed record RemoteButtonBinding(
    RemoteButtonAction Action,
    string? Argument = null);

public sealed record RemoteButtonBindingEntry(
    RemoteButton Button,
    RemoteButtonTrigger Trigger,
    RemoteButtonBinding Binding);

public sealed class RemoteBindingProfile
{
    private static readonly RemoteButtonBinding DisabledBinding =
        new(RemoteButtonAction.Disabled);

    public RemoteBindingProfile(
        string name,
        IEnumerable<RemoteButtonBindingEntry> bindings)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(bindings);

        Name = name;
        Bindings = bindings
            .GroupBy(binding => (binding.Button, binding.Trigger))
            .Select(group => group.Last())
            .OrderBy(binding => binding.Button)
            .ThenBy(binding => binding.Trigger)
            .ToArray();
    }

    public string Name { get; }

    public IReadOnlyList<RemoteButtonBindingEntry> Bindings { get; }

    public RemoteButtonBinding Resolve(
        RemoteButton button,
        RemoteButtonTrigger trigger)
    {
        return Bindings.FirstOrDefault(binding =>
                binding.Button == button && binding.Trigger == trigger)?.Binding
            ?? DisabledBinding;
    }

    public RemoteBindingProfile WithBinding(
        RemoteButton button,
        RemoteButtonTrigger trigger,
        RemoteButtonBinding binding)
    {
        ArgumentNullException.ThrowIfNull(binding);
        return new RemoteBindingProfile(
            Name,
            Bindings
                .Where(existing =>
                    existing.Button != button || existing.Trigger != trigger)
                .Append(new RemoteButtonBindingEntry(button, trigger, binding)));
    }
}

public static class RemoteBindingPresets
{
    public static RemoteBindingProfile CreateCodex()
    {
        return Create(
            "Codex",
            (RemoteButton.Power, RemoteButtonAction.ToggleSidebar, null),
            (RemoteButton.Up, RemoteButtonAction.PreviousChat, null),
            (RemoteButton.Left, RemoteButtonAction.PreviousProject, null),
            (RemoteButton.Ok, RemoteButtonAction.SubmitTask, null),
            (RemoteButton.Right, RemoteButtonAction.NextProject, null),
            (RemoteButton.Down, RemoteButtonAction.NextChat, null),
            (RemoteButton.Back, RemoteButtonAction.GoBack, null),
            (RemoteButton.VolumeUp, RemoteButtonAction.SelectModel, null),
            (RemoteButton.Home, RemoteButtonAction.NewChat, null),
            (RemoteButton.VolumeDown, RemoteButtonAction.SelectReasoning, null),
            (RemoteButton.Menu, RemoteButtonAction.OpenCommandMenu, null),
            (RemoteButton.Tv, RemoteButtonAction.OpenProjectTools, null))
            .WithBinding(
                RemoteButton.Tv,
                RemoteButtonTrigger.DoubleClick,
                new RemoteButtonBinding(RemoteButtonAction.InsertSkill, "$"));
    }

    public static RemoteBindingProfile CreateClaudeCode()
    {
        return Create(
            "Claude Code",
            (RemoteButton.Power, RemoteButtonAction.Escape, null),
            (RemoteButton.Up, RemoteButtonAction.ScrollUp, null),
            (RemoteButton.Left, RemoteButtonAction.NavigatePrevious, null),
            (RemoteButton.Ok, RemoteButtonAction.SubmitTask, null),
            (RemoteButton.Right, RemoteButtonAction.NavigateNext, null),
            (RemoteButton.Down, RemoteButtonAction.ScrollDown, null),
            (RemoteButton.Back, RemoteButtonAction.Undo, null),
            (RemoteButton.VolumeUp, RemoteButtonAction.SelectModel, "previous"),
            (RemoteButton.Home, RemoteButtonAction.FocusPrompt, null),
            (RemoteButton.VolumeDown, RemoteButtonAction.SelectModel, "next"),
            (RemoteButton.Menu, RemoteButtonAction.Find, null),
            (RemoteButton.Tv, RemoteButtonAction.InsertSkill, "/"));
    }

    private static RemoteBindingProfile Create(
        string name,
        params (RemoteButton Button, RemoteButtonAction Action, string? Argument)[] bindings)
    {
        return new RemoteBindingProfile(
            name,
            bindings.Select(binding => new RemoteButtonBindingEntry(
                binding.Button,
                RemoteButtonTrigger.SingleClick,
                new RemoteButtonBinding(binding.Action, binding.Argument))));
    }
}
