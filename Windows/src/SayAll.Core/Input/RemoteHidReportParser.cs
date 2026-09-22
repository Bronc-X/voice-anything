namespace SayAll.Core.Input;

public static class RemoteHidReportParser
{
    public static IReadOnlySet<ushort>? ParseUsages(uint reportId, byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);

        if (reportId != 1)
        {
            return null;
        }

        var offset = data.Length == 7 && data[0] == reportId ? 1 : 0;
        var payloadLength = data.Length - offset;
        if (payloadLength == 0 || payloadLength % 2 != 0)
        {
            return null;
        }

        var usages = new HashSet<ushort>();
        for (var index = offset; index < data.Length; index += 2)
        {
            var usage = (ushort)(data[index] | data[index + 1] << 8);
            if (usage != 0)
            {
                usages.Add(usage);
            }
        }

        return usages;
    }
}
