namespace SayAll.Core.Input;

public sealed class RemoteBindingEditorSession
{
    public RemoteBindingEditorSession(RemoteBindingProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        Profile = profile;
    }

    public RemoteBindingProfile Profile { get; private set; }

    public RemoteButton? SelectedButton { get; private set; }

    public RemoteButtonTrigger SelectedTrigger { get; private set; } =
        RemoteButtonTrigger.SingleClick;

    public RemoteButtonBinding CurrentBinding => SelectedButton is RemoteButton button
        ? Profile.Resolve(button, SelectedTrigger)
        : new RemoteButtonBinding(RemoteButtonAction.Disabled);

    public void SelectButton(RemoteButton button)
    {
        SelectedButton = button;
    }

    public void SelectTrigger(RemoteButtonTrigger trigger)
    {
        SelectedTrigger = trigger;
    }

    public void Assign(RemoteButtonBinding binding)
    {
        ArgumentNullException.ThrowIfNull(binding);
        if (SelectedButton is not RemoteButton button)
        {
            throw new InvalidOperationException("Select a physical remote button before assigning an action.");
        }

        Profile = Profile.WithBinding(button, SelectedTrigger, binding);
    }

    public void ApplyProfile(RemoteBindingProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        Profile = profile;
    }
}
