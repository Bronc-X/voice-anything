namespace SayAll.HidBridge.Contracts;

public static class HidBridgeStartupPolicy
{
    public static int GadgetConnectTimeoutSeconds => 5;

    public static int MaximumInjectionAttempts => 2;

    public static bool ShouldRestartDeviceAfterTimeout(int attempt)
    {
        return attempt == 1;
    }
}
