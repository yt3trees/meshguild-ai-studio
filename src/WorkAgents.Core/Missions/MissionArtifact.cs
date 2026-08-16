namespace WorkAgents.Core.Missions;

/// <summary>ミッションが生んだ成果物 (data-model.md Artifact)。</summary>
public sealed record MissionArtifact
{
    public required string ArtifactId { get; init; }

    public required string MissionId { get; init; }

    public required string SourceMessageId { get; init; }

    public string? IterationId { get; init; }

    public string? NodeRunId { get; init; }

    public required string Path { get; init; }

    public required string Summary { get; init; }

    public required string ContentHash { get; init; }

    public DateTimeOffset? DiscardedAt { get; init; }

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}
