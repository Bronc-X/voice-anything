using System.IO;

namespace SayAll.Windows;

public static class HidBridgeSessionLogPath
{
    public static string Create(
        string directory,
        int processId,
        DateTimeOffset startedAt,
        Guid sessionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(processId);

        var fileName =
            $"hid-bridge-{startedAt:yyyyMMdd-HHmmss-fff}-{processId}-{sessionId:N}.jsonl";
        return Path.Combine(directory, fileName);
    }
}
