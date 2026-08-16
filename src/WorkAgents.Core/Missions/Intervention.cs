namespace WorkAgents.Core.Missions;

/// <summary>人による割り込み (data-model.md Intervention)。</summary>
public sealed record Intervention
{
    public required string InterventionId { get; init; }

    public required string MissionId { get; init; }

    public required string MessageId { get; init; }

    /// <summary>特定エージェント宛。全体宛は null。</summary>
    public string? TargetInstanceId { get; init; }

    public required string Body { get; init; }

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>最初にエージェントの入力へ反映された時刻 (FR-013、SC-006)。</summary>
    public DateTimeOffset? AppliedAt { get; init; }

    public string? AppliedToMessageId { get; init; }
}
