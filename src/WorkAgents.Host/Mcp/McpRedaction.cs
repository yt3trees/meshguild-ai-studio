namespace WorkAgents.Host.Mcp;

public static class McpRedaction
{
    private static readonly string[] SensitiveNames = ["api", "token", "secret", "password", "credential", "privatekey", "authorization"];

    public static string SafeName(string? value, string fallback = "unknown")
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        var normalized = value.Trim();
        return SensitiveNames.Any(name => normalized.Contains(name, StringComparison.OrdinalIgnoreCase))
            ? "[redacted]"
            : McpResponseProjector.SafeText(normalized, 256) ?? fallback;
    }

    public static string SafeErrorCode(string? value)
        => string.IsNullOrWhiteSpace(value) ? "unknown_error" : SafeName(value, "unknown_error");
}
