using WorkAgents.Core.Loops;

namespace WorkAgents.Orchestration.Loops;

public sealed record LoopStopDecision(bool ShouldStop, LoopStopReason? Reason)
{
    public static LoopStopDecision Continue() => new(false, null);

    public static LoopStopDecision Stop(LoopStopReason reason) => new(true, reason);
}

/// <summary>Evaluates loop stop conditions in a stable priority order.</summary>
public static class LoopStopConditionEvaluator
{
    public static LoopStopDecision Evaluate(
        int iterationNo,
        int maxIterations,
        bool scorePassed,
        double? costUsedUsd = null,
        double? costLimitUsd = null,
        TimeSpan? elapsed = null,
        TimeSpan? timeLimit = null)
    {
        if (scorePassed)
        {
            return LoopStopDecision.Stop(LoopStopReason.StopConditionMet);
        }
        if (costLimitUsd.HasValue && costUsedUsd >= costLimitUsd.Value)
        {
            return LoopStopDecision.Stop(LoopStopReason.CostLimit);
        }
        if (timeLimit.HasValue && elapsed >= timeLimit.Value)
        {
            return LoopStopDecision.Stop(LoopStopReason.TimeLimit);
        }
        if (iterationNo >= maxIterations)
        {
            return LoopStopDecision.Stop(LoopStopReason.MaxIterations);
        }
        return LoopStopDecision.Continue();
    }
}
