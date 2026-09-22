using System.Security.Cryptography;
using System.Text.Json;

namespace SayAll.HidBridge.Contracts;

public sealed record HidReportMessage(byte ReportId, byte[] Payload);
public sealed record HidHookProbeMessage(
    string Source,
    byte ReportId,
    int PayloadLength,
    string Reason);

public static class HidBridgeMessageCodec
{
    public const int ProtocolVersion = 1;

    public static HidReportMessage? ParseGadgetLine(string jsonLine, string expectedToken)
    {
        if (!TryDecodeToken(expectedToken, out var expectedTokenBytes))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(jsonLine);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("version", out var version) ||
                version.GetInt32() != ProtocolVersion ||
                !root.TryGetProperty("kind", out var kind) ||
                kind.GetString() != "hid_report" ||
                !root.TryGetProperty("token", out var token) ||
                !TryDecodeToken(token.GetString(), out var actualTokenBytes) ||
                !CryptographicOperations.FixedTimeEquals(expectedTokenBytes, actualTokenBytes) ||
                !root.TryGetProperty("report_id", out var reportIdElement) ||
                reportIdElement.GetByte() != 1 ||
                !root.TryGetProperty("payload", out var payloadElement))
            {
                return null;
            }

            var payloadHex = payloadElement.GetString();
            if (payloadHex is null || payloadHex.Length is not (12 or 14 or 16))
            {
                return null;
            }

            var payload = NormalizePayload(reportIdElement.GetByte(), Convert.FromHexString(payloadHex));
            return payload is null ? null : new HidReportMessage(1, payload);
        }
        catch (Exception exception) when (
            exception is JsonException or FormatException or InvalidOperationException or OverflowException)
        {
            return null;
        }
    }

    private static byte[]? NormalizePayload(byte reportId, byte[] payload)
    {
        if (payload.Length == 6)
        {
            return payload;
        }

        if (payload.Length == 7 && payload[0] == reportId)
        {
            return payload[1..];
        }

        if (payload.Length == 8 && payload[0] == reportId && payload[7] == 0)
        {
            return payload[1..7];
        }

        if (payload.Length == 8 && payload[6] == 0 && payload[7] == 0)
        {
            return payload[..6];
        }

        return null;
    }

    public static bool? ParseReadyLine(string jsonLine, string expectedToken)
    {
        if (!TryDecodeToken(expectedToken, out var expectedTokenBytes))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(jsonLine);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("version", out var version) ||
                version.GetInt32() != ProtocolVersion ||
                !root.TryGetProperty("kind", out var kind) ||
                kind.GetString() != "ready" ||
                !root.TryGetProperty("token", out var token) ||
                !TryDecodeToken(token.GetString(), out var actualTokenBytes) ||
                !CryptographicOperations.FixedTimeEquals(expectedTokenBytes, actualTokenBytes) ||
                !root.TryGetProperty("hook_installed", out var hookInstalled) ||
                hookInstalled.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            {
                return null;
            }

            return hookInstalled.GetBoolean();
        }
        catch (Exception exception) when (
            exception is JsonException or InvalidOperationException or OverflowException)
        {
            return null;
        }
    }

    public static HidHookProbeMessage? ParseProbeLine(
        string jsonLine,
        string expectedToken)
    {
        if (!TryDecodeToken(expectedToken, out var expectedTokenBytes))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(jsonLine);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("version", out var version) ||
                version.GetInt32() != ProtocolVersion ||
                !root.TryGetProperty("kind", out var kind) ||
                kind.GetString() != "hook_probe" ||
                !root.TryGetProperty("token", out var token) ||
                !TryDecodeToken(token.GetString(), out var actualTokenBytes) ||
                !CryptographicOperations.FixedTimeEquals(expectedTokenBytes, actualTokenBytes) ||
                !root.TryGetProperty("source", out var sourceElement) ||
                !root.TryGetProperty("report_id", out var reportIdElement) ||
                !root.TryGetProperty("payload_length", out var lengthElement) ||
                !root.TryGetProperty("reason", out var reasonElement))
            {
                return null;
            }

            var source = sourceElement.GetString();
            var reason = reasonElement.GetString();
            var payloadLength = lengthElement.GetInt32();
            if (source is not ("value_changed" or "report_arrived") ||
                reason is not (
                    "accepted" or
                    "wrong_report_id" or
                    "empty_owner" or
                    "empty_vector" or
                    "invalid_length" or
                    "exception") ||
                payloadLength is < -1 or > 64)
            {
                return null;
            }

            return new HidHookProbeMessage(
                source,
                reportIdElement.GetByte(),
                payloadLength,
                reason);
        }
        catch (Exception exception) when (
            exception is JsonException or InvalidOperationException or OverflowException)
        {
            return null;
        }
    }

    private static bool TryDecodeToken(string? value, out byte[] bytes)
    {
        bytes = [];
        if (value is null || value.Length != 64)
        {
            return false;
        }

        try
        {
            bytes = Convert.FromHexString(value);
            return bytes.Length == 32;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
