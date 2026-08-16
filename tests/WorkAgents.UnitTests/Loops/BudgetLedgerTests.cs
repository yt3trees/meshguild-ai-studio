using WorkAgents.Core.Missions;
using WorkAgents.Orchestration.Budgets;

namespace WorkAgents.UnitTests.Loops;

public sealed class BudgetLedgerTests
{
    [Fact]
    public void CanStartTurn_RejectsATurnThatWouldCrossCostLimit()
    {
        var ledger = new BudgetLedger();
        var budget = new Budget { MissionId = "mission", CostLimitUsd = 1, CostUsedUsd = 0.8 };

        var decision = ledger.CanStartTurn(budget, expectedCostUsd: 0.3);

        Assert.False(decision.Allowed);
        Assert.Equal(MissionStopReason.CostLimit, decision.StopReason);
        Assert.Throws<BudgetLimitException>(() => ledger.EnsureCanStartTurn(budget, expectedCostUsd: 0.3));
    }

    [Fact]
    public async Task RecordAsync_PreservesPartialUsageAndPeakAgents()
    {
        var ledger = new BudgetLedger();
        var updated = await ledger.RecordAsync(
            new Budget { MissionId = "mission" },
            costUsd: 0.4,
            duration: TimeSpan.FromSeconds(2),
            iterationCompleted: true,
            activeAgents: 3);

        Assert.Equal(0.4, updated.CostUsedUsd);
        Assert.Equal(2, updated.ElapsedSeconds);
        Assert.Equal(1, updated.IterationsUsed);
        Assert.Equal(3, updated.PeakConcurrentAgents);
    }
}
