namespace SayAll.Core.Onboarding;

public enum OnboardingStep
{
    Remote,
    Audio,
    VoiceTest,
    Controls,
}

public sealed class OnboardingCapabilities
{
    public bool RemoteConnected { get; set; }

    public bool RemoteButtonObserved { get; set; }

    public bool AudioOutputSelected { get; set; }

    public bool AudioReady { get; set; }

    public bool VoiceSessionStarted { get; set; }

    public bool VoiceSamplesReceived { get; set; }

    public bool VoiceSessionEnded { get; set; }

    public bool TranscriptionAppeared { get; set; }

    public bool ManualTranscriptInputObserved { get; set; }

    public bool VoicePlaybackOpened { get; set; }

    public bool VoicePlaybackConfirmed { get; set; }

    public int TestedRemoteButtonCount { get; set; }
}

public static class OnboardingFlowPolicy
{
    public static bool CanContinue(
        OnboardingStep step,
        OnboardingCapabilities capabilities)
    {
        ArgumentNullException.ThrowIfNull(capabilities);

        return step switch
        {
            OnboardingStep.Remote =>
                capabilities.RemoteConnected && capabilities.RemoteButtonObserved,
            OnboardingStep.Audio =>
                capabilities.AudioOutputSelected &&
                capabilities.AudioReady &&
                capabilities.VoiceSessionStarted &&
                capabilities.VoiceSamplesReceived &&
                capabilities.VoiceSessionEnded,
            OnboardingStep.VoiceTest =>
                capabilities.VoiceSessionStarted &&
                capabilities.VoiceSamplesReceived &&
                capabilities.VoiceSessionEnded &&
                ((capabilities.TranscriptionAppeared &&
                  !capabilities.ManualTranscriptInputObserved) ||
                 (capabilities.VoicePlaybackOpened &&
                  capabilities.VoicePlaybackConfirmed)),
            OnboardingStep.Controls => true,
            _ => false,
        };
    }
}
