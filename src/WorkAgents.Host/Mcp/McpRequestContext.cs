namespace WorkAgents.Host.Mcp;

/// <summary>Request-scoped MCP metadata. It is never persisted as an authorization identity.</summary>
public sealed record McpRequestContext(
    string ProtocolVersion,
    string? ClientName,
    string? ClientVersion,
    string? RequestId,
    string? SessionId,
    CancellationToken RequestCancellation);
