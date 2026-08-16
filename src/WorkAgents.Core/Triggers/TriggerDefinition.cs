namespace WorkAgents.Core.Triggers;

/// <summary>トリガー種別。</summary>
public enum TriggerKind
{
    Manual,
    Schedule,
    Interval,
    Event,
}

/// <summary>重複起動時の方針 (FR-033)。</summary>
public enum OverlapPolicy
{
    Skip,
    Queue,
    Parallel,
}

/// <summary>起動判断の結果。</summary>
public enum TriggerDecision
{
    Started,
    Skipped,
    Queued,
    Parallel,
}

/// <summary>ミッションの起動定義 (data-model.md Trigger)。既存 schedules テーブルを置き換える。</summary>
public sealed record TriggerDefinition
{
    public required string TriggerId { get; init; }

    public required string Name { get; init; }

    public required TriggerKind Kind { get; init; }

    public required string TargetKind { get; init; }

    public required string TargetName { get; init; }

    public required string Input { get; init; }

    public string? Cron { get; init; }

    public int? IntervalSeconds { get; init; }

    public OverlapPolicy OverlapPolicy { get; init; } = OverlapPolicy.Skip;

    public bool Enabled { get; init; } = true;

    public string? SecretRef { get; init; }

    public DateTimeOffset? LastRunAt { get; init; }

    public DateTimeOffset? NextRunAt { get; init; }

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAt { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>トリガー起動の記録 (data-model.md TriggerFire)。</summary>
public sealed record TriggerFire
{
    public required string FireId { get; init; }

    public required string TriggerId { get; init; }

    public DateTimeOffset FiredAt { get; init; } = DateTimeOffset.UtcNow;

    public required TriggerDecision Decision { get; init; }

    public required string DecisionReason { get; init; }

    public string? MissionId { get; init; }
}
