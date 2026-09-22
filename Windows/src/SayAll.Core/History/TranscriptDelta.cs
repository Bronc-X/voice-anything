namespace SayAll.Core.History;

/// <summary>Accept only one insertion into the exact input captured at session start.</summary>
public static class TranscriptDelta
{
    public static string? Extract(string before, string after, int selectionStart, int selectionLength)
    {
        if (before.Length > JournalStore.MaximumTextLength || after.Length > JournalStore.MaximumTextLength ||
            selectionStart < 0 || selectionLength < 0 || selectionStart > before.Length ||
            selectionLength > before.Length - selectionStart) return null;
        var prefix = before[..selectionStart];
        var suffix = before[(selectionStart + selectionLength)..];
        if (after.Length < prefix.Length + suffix.Length ||
            !after.StartsWith(prefix, StringComparison.Ordinal) || !after.EndsWith(suffix, StringComparison.Ordinal)) return null;
        var inserted = after.Substring(prefix.Length, after.Length - prefix.Length - suffix.Length).Trim();
        return string.IsNullOrWhiteSpace(inserted) || inserted == before.Substring(selectionStart, selectionLength).Trim()
            ? null : inserted;
    }
}
