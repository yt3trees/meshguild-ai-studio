namespace WorkAgents.Core.Missions;

/// <summary>ミッション単位の上限と消費実績 (data-model.md Budget)。</summary>
public sealed record Budget
{
    public required string MissionId { get; init; }

    public double? CostLimitUsd { get; init; }

    public int? TimeLimitSeconds { get; init; }

    public int? MaxIterations { get; init; }

    public int? MaxConcurrentAgents { get; init; }

    public double CostUsedUsd { get; init; }

    public int ElapsedSeconds { get; init; }

    public int IterationsUsed { get; init; }

    public int PeakConcurrentAgents { get; init; }
}
