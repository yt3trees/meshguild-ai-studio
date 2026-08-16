using WorkAgents.Core.Triggers;

namespace WorkAgents.Orchestration.Triggers;

public sealed record OverlapDecision(TriggerDecision Decision, string Reason)
{
    public bool StartsMission => Decision is TriggerDecision.Started or TriggerDecision.Parallel or TriggerDecision.Queued;
}

public static class OverlapPolicyDecider
{
    public static OverlapDecision Decide(bool activeMission, OverlapPolicy policy)
    {
        if (!activeMission)
        {
            return new OverlapDecision(TriggerDecision.Started, "no overlapping mission");
        }
        return policy switch
        {
            OverlapPolicy.Skip => new OverlapDecision(TriggerDecision.Skipped, "overlap policy skip"),
            OverlapPolicy.Queue => new OverlapDecision(TriggerDecision.Queued, "overlap policy queue"),
            OverlapPolicy.Parallel => new OverlapDecision(TriggerDecision.Parallel, "overlap policy parallel"),
            _ => new OverlapDecision(TriggerDecision.Skipped, "unknown overlap policy"),
        };
    }
}
