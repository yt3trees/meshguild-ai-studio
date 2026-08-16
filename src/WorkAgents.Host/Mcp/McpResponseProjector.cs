namespace WorkAgents.Host.Mcp;

public static class McpResponseProjector
{
    public static string? SafeText(string? value, int maxLength = 2000)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var singleLine = value.ReplaceLineEndings(" ").Trim();
        return singleLine.Length <= maxLength ? singleLine : singleLine[..maxLength] + "...";
    }

    public static IReadOnlyList<T> Page<T>(IEnumerable<T> values, int offset, int limit, out int? nextOffset)
    {
        ArgumentNullException.ThrowIfNull(values);
        var safeOffset = Math.Max(0, offset);
        var safeLimit = Math.Max(1, limit);
        var page = values.Skip(safeOffset).Take(safeLimit + 1).ToArray();
        nextOffset = page.Length > safeLimit ? safeOffset + safeLimit : null;
        return page.Take(safeLimit).ToArray();
    }
}
