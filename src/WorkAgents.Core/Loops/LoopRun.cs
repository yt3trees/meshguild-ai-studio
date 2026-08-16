namespace WorkAgents.Core.Loops;

/// <summary>ループ停止理由 (FR-022)。</summary>
public enum LoopStopReason
{
    StopConditionMet,
    MaxIterations,
    CostLimit,
    TimeLimit,
    UserBreak,
}

/// <summary>反復の状態。</summary>
public enum IterationState
{
    Running,
    Succeeded,
    Failed,
    Discarded,
}

/// <summary>評価者種別。</summary>
public enum EvaluatorKind
{
    Agent,
    Deterministic,
}

/// <summary>ループノードの 1 回の実行 (data-model.md LoopRun)。</summary>
public sealed record LoopRun
{
    public required string LoopRunId { get; init; }

    public required string MissionId { get; init; }

    public required string NodeRunId { get; init; }

    public int MaxIterations { get; init; } = 10;

    public double? CostLimitUsd { get; init; }

    public int? TimeLimitSeconds { get; init; }

    public double? ScoreThreshold { get; init; }

    public LoopStopReason? StopReason { get; init; }

    public string? BestIterationId { get; init; }

    public DateTimeOffset StartedAt { get; init; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? CompletedAt { get; init; }
}

/// <summary>ループの 1 周 (data-model.md Iteration)。</summary>
public sealed record Iteration
{
    public required string IterationId { get; init; }

    public required string LoopRunId { get; init; }

    public required int IterationNo { get; init; }

    public string? InputJson { get; init; }

    public string? OutputJson { get; init; }

    public IterationState State { get; init; } = IterationState.Running;

    public double CostUsd { get; init; }

    public long Tokens { get; init; }

    public long DurationMs { get; init; }

    public DateTimeOffset? DiscardedAt { get; init; }

    public DateTimeOffset StartedAt { get; init; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? CompletedAt { get; init; }
}

/// <summary>1 反復の評価結果 (data-model.md Evaluation)。</summary>
public sealed record Evaluation
{
    public required string EvaluationId { get; init; }

    public required string IterationId { get; init; }

    public required double Score { get; init; }

    public required EvaluatorKind EvaluatorKind { get; init; }

    public required string EvaluatorRef { get; init; }

    public string? Notes { get; init; }

    public bool Passed { get; init; }

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>1 評価指標 (data-model.md EvaluationMetric)。</summary>
public sealed record EvaluationMetric
{
    public required string MetricId { get; init; }

    public required string EvaluationId { get; init; }

    public required string Name { get; init; }

    public required double Value { get; init; }

    public required double Target { get; init; }

    public bool Achieved { get; init; }

    public string? Unit { get; init; }
}
