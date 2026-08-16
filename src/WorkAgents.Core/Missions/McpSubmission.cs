namespace WorkAgents.Core.Missions;

/// <summary>Idempotency record for a Mission submitted through MCP.</summary>
public sealed record McpSubmission
{
    public required string RequestKey { get; init; }

    public required string RequestHash { get; init; }

    public required string MissionId { get; init; }

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}
