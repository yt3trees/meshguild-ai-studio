namespace WorkAgents.Core.Missions;

/// <summary>ミッションの起動対象種別。</summary>
public enum MissionTargetKind
{
    Team,
    Graph,
}

/// <summary>ミッションの起動元種別 (FR-032)。</summary>
public enum MissionTriggerKind
{
    Manual,
    Schedule,
    Interval,
    Event,
}

/// <summary>ミッションのライフサイクル状態 (data-model.md)。</summary>
public enum MissionStatus
{
    Queued,
    Running,
    Paused,
    AwaitingApproval,
    Succeeded,
    NotConverged,
    Failed,
    Aborted,
}

/// <summary>ミッションの結果種別 (FR-042)。終端状態でのみ非 null。</summary>
public enum MissionOutcome
{
    Succeeded,
    NotConverged,
    Failed,
    Aborted,
}

/// <summary>ミッション停止理由。</summary>
public enum MissionStopReason
{
    StopConditionMet,
    MaxIterations,
    CostLimit,
    TimeLimit,
    NoProgress,
    Deadlock,
    UserAbort,
    OrchestratorFailure,
    NoCheckpoint,
}

/// <summary>待機理由 (FR-058)。</summary>
public enum MissionQueuedReason
{
    ConcurrencyLimit,
    OverlapPolicy,
}

/// <summary>人が投入する 1 つの目標。従来の Run の上位概念 (data-model.md Mission)。</summary>
public sealed record Mission
{
    public required string MissionId { get; init; }

    public required string Goal { get; init; }

    public required MissionTargetKind TargetKind { get; init; }

    public required string TargetName { get; init; }

    public string? GraphVersionId { get; init; }

    public string? TeamName { get; init; }

    public MissionStatus Status { get; init; } = MissionStatus.Queued;

    public string? TriggerId { get; init; }

    public MissionTriggerKind TriggerKind { get; init; } = MissionTriggerKind.Manual;

    public MissionQueuedReason? QueuedReason { get; init; }

    public int? QueuePosition { get; init; }

    public MissionOutcome? Outcome { get; init; }

    public MissionStopReason? StopReason { get; init; }

    public string? Error { get; init; }

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? StartedAt { get; init; }

    public DateTimeOffset? CompletedAt { get; init; }
}
