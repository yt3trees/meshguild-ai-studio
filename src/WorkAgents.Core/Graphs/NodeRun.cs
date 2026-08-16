namespace WorkAgents.Core.Graphs;

/// <summary>NodeRun の状態 (FR-029)。</summary>
public enum NodeRunState
{
    Pending,
    Running,
    Waiting,
    Succeeded,
    Failed,
    Skipped,
    Unreached,
}

/// <summary>グラフ上の 1 ノードの 1 回の実行 (data-model.md NodeRun)。</summary>
public sealed record NodeRun
{
    public required string NodeRunId { get; init; }

    public required string MissionId { get; init; }

    public required string NodeId { get; init; }

    public required NodeKind NodeKind { get; init; }

    public NodeRunState State { get; init; } = NodeRunState.Pending;

    public string? ParentNodeRunId { get; init; }

    public int? IterationNo { get; init; }

    public string? InputJson { get; init; }

    public string? OutputJson { get; init; }

    public string? Error { get; init; }

    public DateTimeOffset? StartedAt { get; init; }

    public DateTimeOffset? CompletedAt { get; init; }
}

/// <summary>ノード間のエッジ通過記録 (data-model.md EdgeTransit)。</summary>
public sealed record EdgeTransit
{
    public required string EdgeTransitId { get; init; }

    public required string MissionId { get; init; }

    public required string EdgeId { get; init; }

    public required string FromNodeRunId { get; init; }

    public required string ToNodeRunId { get; init; }

    public string? ConditionResult { get; init; }

    public DateTimeOffset TransitedAt { get; init; } = DateTimeOffset.UtcNow;
}
