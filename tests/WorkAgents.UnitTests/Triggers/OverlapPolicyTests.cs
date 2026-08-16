using WorkAgents.Core.Triggers;
using WorkAgents.Orchestration.Triggers;

namespace WorkAgents.UnitTests.Triggers;

public sealed class OverlapPolicyTests
{
    [Theory]
    [InlineData(OverlapPolicy.Skip, TriggerDecision.Skipped)]
    [InlineData(OverlapPolicy.Queue, TriggerDecision.Queued)]
    [InlineData(OverlapPolicy.Parallel, TriggerDecision.Parallel)]
    public void ActiveMissionUsesConfiguredOverlapPolicy(OverlapPolicy policy, TriggerDecision expected)
    {
        var decision = OverlapPolicyDecider.Decide(activeMission: true, policy);

        Assert.Equal(expected, decision.Decision);
        Assert.False(string.IsNullOrWhiteSpace(decision.Reason));
    }
}
