using SayAll.Core.Input;

namespace SayAll.Windows;

public static class SayAllSettingsMigration
{
    public const int CurrentVersion = 4;

    public static bool Apply(SayAllSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (settings.ConfigurationVersion >= CurrentVersion)
        {
            return false;
        }

        if (settings.ConfigurationVersion < 1)
        {
            settings.PreferredInputMethodId = WindowsVoiceInputCatalog.TypelessId;
            if (string.Equals(settings.ActivePreset, "Codex", StringComparison.OrdinalIgnoreCase))
            {
                var profile = CreateCurrentProfile(settings);
                var requestedDefaults = RemoteBindingPresets.CreateCodex();
                foreach (var button in new[]
                         {
                             RemoteButton.Up,
                             RemoteButton.Down,
                             RemoteButton.Left,
                             RemoteButton.Right,
                             RemoteButton.VolumeUp,
                             RemoteButton.VolumeDown,
                             RemoteButton.Back,
                             RemoteButton.Home,
                         })
                {
                    profile = profile.WithBinding(
                        button,
                        RemoteButtonTrigger.SingleClick,
                        requestedDefaults.Resolve(button, RemoteButtonTrigger.SingleClick));
                }

                settings.RemoteBindings = profile.Bindings.ToList();
            }
        }

        if (settings.ConfigurationVersion < 2 &&
            string.Equals(settings.ActivePreset, "Codex", StringComparison.OrdinalIgnoreCase))
        {
            var profile = CreateCurrentProfile(settings);
            var requestedDefaults = RemoteBindingPresets.CreateCodex();
            profile = profile
                .WithBinding(
                    RemoteButton.Back,
                    RemoteButtonTrigger.SingleClick,
                    requestedDefaults.Resolve(RemoteButton.Back, RemoteButtonTrigger.SingleClick))
                .WithBinding(
                    RemoteButton.Tv,
                    RemoteButtonTrigger.SingleClick,
                    requestedDefaults.Resolve(RemoteButton.Tv, RemoteButtonTrigger.SingleClick));
            if (profile.Resolve(RemoteButton.Tv, RemoteButtonTrigger.DoubleClick).Action ==
                RemoteButtonAction.Disabled)
            {
                profile = profile.WithBinding(
                    RemoteButton.Tv,
                    RemoteButtonTrigger.DoubleClick,
                    requestedDefaults.Resolve(RemoteButton.Tv, RemoteButtonTrigger.DoubleClick));
            }

            settings.RemoteBindings = profile.Bindings.ToList();
        }

        if (settings.ConfigurationVersion < 3 &&
            string.Equals(settings.ActivePreset, "Codex", StringComparison.OrdinalIgnoreCase))
        {
            var profile = CreateCurrentProfile(settings);
            var requestedDefaults = RemoteBindingPresets.CreateCodex();
            foreach (var button in RemoteButtonCatalog.StandardButtons)
            {
                profile = profile.WithBinding(
                    button,
                    RemoteButtonTrigger.SingleClick,
                    requestedDefaults.Resolve(button, RemoteButtonTrigger.SingleClick));
            }

            settings.RemoteBindings = profile.Bindings.ToList();
        }

        if (settings.ConfigurationVersion < 4 &&
            string.Equals(settings.ActivePreset, "Codex", StringComparison.OrdinalIgnoreCase))
        {
            var profile = CreateCurrentProfile(settings);
            var requestedDefaults = RemoteBindingPresets.CreateCodex();
            foreach (var button in new[]
                     {
                         RemoteButton.VolumeUp,
                         RemoteButton.VolumeDown,
                         RemoteButton.Tv,
                     })
            {
                profile = profile.WithBinding(
                    button,
                    RemoteButtonTrigger.SingleClick,
                    requestedDefaults.Resolve(button, RemoteButtonTrigger.SingleClick));
            }

            settings.RemoteBindings = profile.Bindings.ToList();
        }

        settings.ConfigurationVersion = CurrentVersion;
        return true;
    }

    private static RemoteBindingProfile CreateCurrentProfile(SayAllSettings settings)
    {
        return new RemoteBindingProfile(
            "Codex",
            settings.RemoteBindings.Count > 0
                ? settings.RemoteBindings
                : RemoteBindingPresets.CreateCodex().Bindings);
    }
}
