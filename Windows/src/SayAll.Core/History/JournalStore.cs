using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SayAll.Core.History;

public sealed record UsageDay(string Date, long ButtonPresses, double VoiceSeconds, long VoiceSessions);
public sealed record Reflection(string Id, string SessionId, DateTimeOffset StartedAt,
    DateTimeOffset EndedAt, string Application, string DeviceModel, string Text, string CaptureMethod);
public sealed record AgentGrant(string Id, string Name, string TokenHash, DateTimeOffset CreatedAt);
public sealed record CreatedAgentGrant(AgentGrant Grant, string Token);
public sealed record JournalDocument
{
    public int SchemaVersion { get; init; } = 1;
    public bool RecordReflections { get; init; }
    public bool AgentAccessEnabled { get; init; }
    public List<UsageDay> Days { get; init; } = [];
    public List<Reflection> Reflections { get; init; } = [];
    public List<AgentGrant> Grants { get; init; } = [];
}
public sealed record UsageSummary(long ButtonPresses, double VoiceSeconds, long VoiceSessions,
    IReadOnlyList<UsageDay> Days);

/// <summary>Single-writer local store. MCP readers only observe complete atomic snapshots.</summary>
public sealed class JournalStore
{
    public const int MaximumTextLength = 32_768;
    public const int MaximumReflections = 10_000;
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow,
    };
    private readonly string path;
    private readonly object gate = new();

    public JournalStore(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        path = Path.Combine(Path.GetFullPath(directory), "journal.json");
    }

    public static string DefaultDirectory => OperatingSystem.IsMacOS()
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support", "VoiceAnything")
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VoiceAnything");

    public JournalDocument Read()
    {
        lock (gate)
        {
            if (!File.Exists(path)) return new();
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            if (stream.Length > 64 * 1024 * 1024) throw new InvalidDataException("本地记录文件过大。");
            var document = JsonSerializer.Deserialize<JournalDocument>(stream, JsonOptions)
                ?? throw new InvalidDataException("本地记录为空。");
            if (document.SchemaVersion != 1 || document.Days is null ||
                document.Reflections is null || document.Grants is null ||
                document.Days.Any(day => day is null || !ValidDate(day.Date) || day.ButtonPresses < 0 ||
                    !double.IsFinite(day.VoiceSeconds) || day.VoiceSeconds < 0 || day.VoiceSessions < 0) ||
                document.Days.Select(day => day.Date).Distinct().Count() != document.Days.Count ||
                document.Reflections.Count > MaximumReflections || document.Grants.Count > 32 ||
                document.Reflections.Any(record => record is null || !ValidReflection(record)) ||
                document.Reflections.Select(record => record.Id).Distinct().Count() != document.Reflections.Count ||
                document.Reflections.Select(record => record.SessionId).Distinct().Count() != document.Reflections.Count ||
                document.Grants.Any(grant => grant is null || grant.TokenHash is null ||
                    !Guid.TryParse(grant.Id, out _) || string.IsNullOrWhiteSpace(grant.Name) || grant.Name.Length > 80 ||
                    grant.TokenHash.Length != 64 || grant.TokenHash.Any(character => !Uri.IsHexDigit(character))))
                throw new InvalidDataException("本地记录格式无效或版本不受支持。");
            return document;
        }
    }

    public void SetPrivacy(bool reflections, bool agentAccess) => Update(document => document with
    { RecordReflections = reflections, AgentAccessEnabled = agentAccess });

    public void RecordButton(DateTimeOffset timestamp, TimeZoneInfo zone) => Update(document =>
    {
        var date = DateKey(timestamp, zone);
        var days = document.Days.ToDictionary(day => day.Date, StringComparer.Ordinal);
        var day = days.GetValueOrDefault(date) ?? new UsageDay(date, 0, 0, 0);
        days[date] = day with { ButtonPresses = checked(day.ButtonPresses + 1) };
        return document with { Days = days.Values.OrderBy(day => day.Date).ToList() };
    });

    public void RecordVoice(DateTimeOffset start, DateTimeOffset end, double audioSeconds, TimeZoneInfo zone)
    {
        if (end < start || !double.IsFinite(audioSeconds) || audioSeconds <= 0 ||
            audioSeconds > 24 * 60 * 60 || end - start > TimeSpan.FromDays(2))
            throw new ArgumentOutOfRangeException(nameof(audioSeconds));
        Update(document =>
        {
            var days = document.Days.ToDictionary(day => day.Date, StringComparer.Ordinal);
            var cursor = start;
            var first = true;
            do
            {
                var date = DateKey(cursor, zone);
                var local = TimeZoneInfo.ConvertTime(cursor, zone).DateTime;
                var midnight = DateTime.SpecifyKind(local.Date.AddDays(1), DateTimeKind.Unspecified);
                while (zone.IsInvalidTime(midnight)) midnight = midnight.AddMinutes(1);
                var boundary = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(midnight, zone));
                var segmentEnd = boundary < end ? boundary : end;
                var fraction = end == start ? 1 : (segmentEnd - cursor).TotalSeconds / (end - start).TotalSeconds;
                var day = days.GetValueOrDefault(date) ?? new UsageDay(date, 0, 0, 0);
                days[date] = day with
                {
                    VoiceSeconds = day.VoiceSeconds + audioSeconds * fraction,
                    VoiceSessions = checked(day.VoiceSessions + (first ? 1 : 0)),
                };
                first = false;
                cursor = segmentEnd;
            } while (cursor < end);
            return document with { Days = days.Values.OrderBy(day => day.Date).ToList() };
        });
    }

    public UsageSummary Summarize(string? from = null, string? through = null)
    {
        ValidateRange(from, through);
        var days = Read().Days.Where(day => InRange(day.Date, from, through)).ToArray();
        return new(days.Sum(day => day.ButtonPresses), days.Sum(day => day.VoiceSeconds),
            days.Sum(day => day.VoiceSessions), days);
    }

    public bool Append(Reflection record)
    {
        if (!ValidReflection(record))
            throw new ArgumentException("回眸记录无效。", nameof(record));
        var added = false;
        Update(document =>
        {
            if (!document.RecordReflections || document.Reflections.Any(item => item.SessionId == record.SessionId))
                return document;
            if (document.Reflections.Count >= MaximumReflections)
                throw new InvalidOperationException("回眸已达 10000 条，请先导出并删除不再需要的记录。");
            document.Reflections.Add(record);
            added = true;
            return document;
        });
        return added;
    }

    public IReadOnlyList<Reflection> Search(string query = "", string? application = null,
        string? from = null, string? through = null, int limit = 50, int offset = 0)
    {
        ValidateRange(from, through);
        if (query.Length > 256 || application?.Length > 256 || limit is < 1 or > 100 || offset is < 0 or > MaximumReflections)
            throw new ArgumentOutOfRangeException(nameof(limit));
        return Read().Reflections.Where(item =>
                (string.IsNullOrEmpty(application) || item.Application.Equals(application, StringComparison.OrdinalIgnoreCase)) &&
                item.Text.Contains(query, StringComparison.OrdinalIgnoreCase) &&
                InRange(item.EndedAt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), from, through))
            .OrderByDescending(item => item.EndedAt).ThenBy(item => item.Id, StringComparer.Ordinal)
            .Skip(offset).Take(limit).ToArray();
    }

    public bool DeleteReflection(string id)
    {
        var deleted = false;
        Update(document => { deleted = document.Reflections.RemoveAll(item => item.Id == id) > 0; return document; });
        return deleted;
    }

    public CreatedAgentGrant GrantAgent(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 80) throw new ArgumentException("请输入客户端名称。");
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var grant = new AgentGrant(Guid.NewGuid().ToString(), name.Trim(), HashToken(token), DateTimeOffset.UtcNow);
        Update(document =>
        {
            if (!document.AgentAccessEnabled) throw new InvalidOperationException("请先开启本地 Agent 访问。");
            if (document.Grants.Count >= 32) throw new InvalidOperationException("最多授权 32 个客户端。");
            document.Grants.Add(grant);
            return document;
        });
        return new(grant, token);
    }

    public void RevokeAgent(string id) => Update(document =>
    { document.Grants.RemoveAll(grant => grant.Id == id); return document; });

    public bool IsAuthorized(string? token)
    {
        if (token is null || token.Length != 64 || token.Any(character => !Uri.IsHexDigit(character))) return false;
        var document = Read();
        if (!document.AgentAccessEnabled) return false;
        var hash = Encoding.ASCII.GetBytes(HashToken(token));
        return document.Grants.Any(grant => CryptographicOperations.FixedTimeEquals(hash, Encoding.ASCII.GetBytes(grant.TokenHash)));
    }

    private void Update(Func<JournalDocument, JournalDocument> transform)
    {
        lock (gate)
        {
            var updated = transform(Read());
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    JsonSerializer.Serialize(stream, updated, JsonOptions);
                    if (stream.Length > 64 * 1024 * 1024)
                        throw new InvalidOperationException("本地记录已达容量上限，请先导出并删除不再需要的回眸。");
                    stream.Flush(flushToDisk: true);
                }
                File.Move(temporary, path, overwrite: true);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }

    private static string HashToken(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    private static bool ValidReflection(Reflection record) =>
        Guid.TryParse(record.Id, out _) && Guid.TryParse(record.SessionId, out _) && record.EndedAt >= record.StartedAt &&
        !string.IsNullOrWhiteSpace(record.Text) && record.Text.Length <= MaximumTextLength &&
        !string.IsNullOrWhiteSpace(record.Application) && record.Application.Length <= 256 &&
        record.DeviceModel is not null && record.DeviceModel.Length <= 128 &&
        record.CaptureMethod is "accessibility-delta-v1" or "manual";
    private static string DateKey(DateTimeOffset value, TimeZoneInfo zone) =>
        TimeZoneInfo.ConvertTime(value, zone).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    private static bool ValidDate(string? value) => DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _);
    private static bool InRange(string value, string? from, string? through) =>
        (from is null || string.CompareOrdinal(value, from) >= 0) && (through is null || string.CompareOrdinal(value, through) <= 0);
    private static void ValidateRange(string? from, string? through)
    {
        if ((from is not null && !ValidDate(from)) || (through is not null && !ValidDate(through)) ||
            (from is not null && through is not null && string.CompareOrdinal(from, through) > 0))
            throw new ArgumentException("日期范围无效，请使用 YYYY-MM-DD。");
    }
}
