namespace WorkAgents.Core.Missions;

/// <summary>チェックポイントの境界種別。</summary>
public enum CheckpointBoundaryKind
{
    Iteration,
    Node,
}

/// <summary>反復またはノードの完了時点で保存される再開位置 (data-model.md Checkpoint)。</summary>
public sealed record Checkpoint
{
    public required string CheckpointId { get; init; }

    public required string MissionId { get; init; }

    public required CheckpointBoundaryKind BoundaryKind { get; init; }

    public string? NodeRunId { get; init; }

    public string? IterationId { get; init; }

    public required long LastMessageSeq { get; init; }

    /// <summary>論理状態 (実行位置、変数、実行時編成、予算消費)。</summary>
    public required string StateJson { get; init; }

    public string? WorkspacePath { get; init; }

    public bool WorkspaceRestorable { get; init; }

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}
