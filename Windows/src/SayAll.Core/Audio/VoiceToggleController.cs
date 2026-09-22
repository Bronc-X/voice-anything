namespace SayAll.Core.Audio;

public enum VoiceToggleState
{
    Idle,
    Starting,
    Recording,
    Stopping,
}

public enum VoiceToggleAction
{
    None,
    RequestInputMethodSelection,
    OpenMicrophone,
    CloseMicrophone,
}

public sealed class VoiceToggleController
{
    public VoiceToggleState State { get; private set; } = VoiceToggleState.Idle;

    public VoiceToggleAction OnMicrophoneButtonPressed(bool hasSelectedInputMethod)
    {
        if (!hasSelectedInputMethod)
        {
            return VoiceToggleAction.RequestInputMethodSelection;
        }

        switch (State)
        {
            case VoiceToggleState.Idle:
                State = VoiceToggleState.Starting;
                return VoiceToggleAction.OpenMicrophone;
            case VoiceToggleState.Starting:
            case VoiceToggleState.Recording:
                State = VoiceToggleState.Stopping;
                return VoiceToggleAction.CloseMicrophone;
            default:
                return VoiceToggleAction.None;
        }
    }

    public void NotifyVoiceStarted()
    {
        if (State == VoiceToggleState.Starting)
        {
            State = VoiceToggleState.Recording;
        }
    }

    public void NotifyVoiceStopped()
    {
        State = VoiceToggleState.Idle;
    }

    public void Reset()
    {
        State = VoiceToggleState.Idle;
    }
}
