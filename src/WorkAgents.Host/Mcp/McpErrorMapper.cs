namespace WorkAgents.Host.Mcp;

public sealed record McpToolError(string Code, string Message, string? NextAction = null);

public static class McpErrorMapper
{
    public static McpToolError InvalidInput(string message) => new("invalid_input", message, "Correct the request and retry.");

    public static McpToolError UnknownTarget(string message) => new("unknown_target", message, "List definitions and choose an available target.");

    public static McpToolError NotFound(string message) => new("not_found", message, "Refresh the identifier and retry.");

    public static McpToolError FromException(Exception exception)
    {
        var message = exception is ArgumentException or InvalidOperationException
            ? McpResponseProjector.SafeText(exception.Message, 500)
            : "The requested operation failed.";
        return new McpToolError("operation_failed", message ?? "The requested operation failed.", "Review the request or retry later.");
    }
}
