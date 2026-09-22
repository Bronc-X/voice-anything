using System.Text.Json;

namespace SayAll.HidBridge.Contracts;

public sealed record HidBridgeLogEvent(
    string Kind,
    byte ReportId,
    byte[] Payload,
    string? Detail = null,
    DateTimeOffset Timestamp = default,
    string? DeviceKey = null);

public static class HidBridgeLogEventCodec
{
    private static readonly IReadOnlySet<string> StateEvents = new HashSet<string>(
        StringComparer.Ordinal)
    {
        "bridge_starting",
        "remote_host_found",
        "remote_host_verified",
        "runtime_verified",
        "gadget_injected",
        "gadget_connect_timeout",
        "device_restart_started",
        "device_restart_completed",
        "gadget_connected",
        "hook_loading",
        "hook_ready",
        "bridge_stopped",
        "bridge_error",
    };

    public static int GetInitialCursor(string content)
    {
        ArgumentNullException.ThrowIfNull(content);
        return content.Split(
            ['\r', '\n'],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Length;
    }

    public static HidBridgeLogEvent? ParseLine(string jsonLine)
    {
        if (string.IsNullOrWhiteSpace(jsonLine))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(jsonLine);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("event", out var eventElement) ||
                eventElement.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            var kind = eventElement.GetString();
            var timestamp = ParseTimestamp(root);
            if (kind == "hid_report")
            {
                return ParseHidReport(root, timestamp) is { } report ? report with { DeviceKey = ParseDeviceKey(root) } : null;
            }

            if (kind is null || !StateEvents.Contains(kind))
            {
                return null;
            }

            string? detail = null;
            if (kind == "bridge_error" &&
                root.TryGetProperty("detail", out var detailElement) &&
                detailElement.ValueKind == JsonValueKind.String)
            {
                detail = detailElement.GetString();
            }

            return new HidBridgeLogEvent(kind, 0, [], detail, timestamp, ParseDeviceKey(root));
        }
        catch (Exception exception) when (
            exception is JsonException or FormatException or InvalidOperationException or OverflowException)
        {
            return null;
        }
    }

    private static HidBridgeLogEvent? ParseHidReport(
        JsonElement root,
        DateTimeOffset timestamp)
    {
        if (!root.TryGetProperty("detail", out var detail) ||
            detail.ValueKind != JsonValueKind.Object ||
            !detail.TryGetProperty("report_id", out var reportIdElement) ||
            reportIdElement.GetByte() != 1 ||
            !detail.TryGetProperty("payload", out var payloadElement) ||
            payloadElement.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var payloadHex = payloadElement.GetString();
        if (payloadHex is null || payloadHex.Length != 12)
        {
            return null;
        }

        var payload = Convert.FromHexString(payloadHex);
        return payload.Length == 6
            ? new HidBridgeLogEvent("hid_report", 1, payload, Timestamp: timestamp)
            : null;
    }

    private static DateTimeOffset ParseTimestamp(JsonElement root)
    {
        return root.TryGetProperty("timestamp", out var timestampElement) &&
               timestampElement.ValueKind == JsonValueKind.String &&
               DateTimeOffset.TryParse(
                   timestampElement.GetString(),
                   System.Globalization.CultureInfo.InvariantCulture,
                   System.Globalization.DateTimeStyles.RoundtripKind,
                   out var timestamp)
            ? timestamp
            : default;
    }

    private static string? ParseDeviceKey(JsonElement root) =>
        root.TryGetProperty("device_key", out var value) && value.ValueKind == JsonValueKind.String &&
        value.GetString() is { Length: 64 } key && key.All(Uri.IsHexDigit) ? key.ToUpperInvariant() : null;
}
