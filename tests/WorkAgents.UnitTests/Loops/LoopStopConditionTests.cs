using WorkAgents.Core.Loops;
using WorkAgents.Orchestration.Loops;

namespace WorkAgents.UnitTests.Loops;

public sealed class LoopStopConditionTests
{
    [Fact]
    public void ScoreStopsBeforeIterationLimit()
    {
        var result = LoopStopConditionEvaluator.Evaluate(2, 10, scorePassed: true);

        Assert.True(result.ShouldStop);
        Assert.Equal(LoopStopReason.StopConditionMet, result.Reason);
    }

    [Fact]
    public void CostAndTimeLimitsHaveDistinctReasons()
    {
        Assert.Equal(
            LoopStopReason.CostLimit,
            LoopStopConditionEvaluator.Evaluate(1, 10, false, 1, 1).Reason);
        Assert.Equal(
            LoopStopReason.TimeLimit,
            LoopStopConditionEvaluator.Evaluate(1, 10, false, elapsed: TimeSpan.FromSeconds(10), timeLimit: TimeSpan.FromSeconds(10)).Reason);
        Assert.Equal(
            LoopStopReason.MaxIterations,
            LoopStopConditionEvaluator.Evaluate(3, 3, false).Reason);
    }
}
