using WorkAgents.Core.Abstractions;
using WorkAgents.Core.Missions;

namespace WorkAgents.Orchestration.Budgets;

public sealed record BudgetDecision(bool Allowed, MissionStopReason? StopReason, string Code)
{
    public static BudgetDecision Allow() => new(true, null, "ok");

    public static BudgetDecision Reject(MissionStopReason reason, string code) => new(false, reason, code);
}

public sealed class BudgetLimitException : InvalidOperationException
{
    public BudgetLimitException(MissionStopReason reason, string message) : base(message)
    {
        StopReason = reason;
    }

    public MissionStopReason StopReason { get; }
}

/// <summary>Checks mission limits before each turn and persists usage after it completes.</summary>
public sealed class BudgetLedger
{
    private readonly IBudgetStore? _store;

    public BudgetLedger(IBudgetStore? store = null)
    {
        _store = store;
    }

    public BudgetDecision CanStartTurn(
        Budget budget,
        double expectedCostUsd = 0,
        TimeSpan? expectedDuration = null)
    {
        ArgumentNullException.ThrowIfNull(budget);
        if (budget.CostLimitUsd.HasValue && budget.CostUsedUsd + expectedCostUsd > budget.CostLimitUsd.Value)
        {
            return BudgetDecision.Reject(MissionStopReason.CostLimit, "cost_limit");
        }
        if (budget.TimeLimitSeconds.HasValue
            && budget.ElapsedSeconds + (int)Math.Ceiling((expectedDuration ?? TimeSpan.Zero).TotalSeconds) > budget.TimeLimitSeconds.Value)
        {
            return BudgetDecision.Reject(MissionStopReason.TimeLimit, "time_limit");
        }
        return BudgetDecision.Allow();
    }

    public void EnsureCanStartTurn(Budget budget, double expectedCostUsd = 0, TimeSpan? expectedDuration = null)
    {
        var decision = CanStartTurn(budget, expectedCostUsd, expectedDuration);
        if (!decision.Allowed)
        {
            throw new BudgetLimitException(decision.StopReason!.Value, $"Mission budget limit reached: {decision.Code}.");
        }
    }

    public async Task<Budget> RecordAsync(
        Budget budget,
        double costUsd,
        TimeSpan duration,
        bool iterationCompleted,
        int activeAgents = 0,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(budget);
        var updated = budget with
        {
            CostUsedUsd = budget.CostUsedUsd + Math.Max(0, costUsd),
            ElapsedSeconds = budget.ElapsedSeconds + Math.Max(0, (int)Math.Ceiling(duration.TotalSeconds)),
            IterationsUsed = budget.IterationsUsed + (iterationCompleted ? 1 : 0),
            PeakConcurrentAgents = Math.Max(budget.PeakConcurrentAgents, activeAgents),
        };
        if (_store is not null)
        {
            await _store.UpsertAsync(updated, ct);
        }
        return updated;
    }
}
