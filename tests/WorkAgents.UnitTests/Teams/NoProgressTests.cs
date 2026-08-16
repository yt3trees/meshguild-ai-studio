using WorkAgents.Core.Teams;
using WorkAgents.Orchestration.Teams;

namespace WorkAgents.UnitTests.Teams;

public sealed class NoProgressTests
{
    [Fact]
    public void ProgressResetsTheStalledTurnCounter()
    {
        var policy = new ConversationPolicy(new TeamDefinition
        {
            Name = "team",
            Orchestrator = new TeamOrchestrator { Agent = "orchestrator" },
            Members = [new TeamMember { Agent = "a" }, new TeamMember { Agent = "b" }],
            Limits = new TeamLimits { NoProgressRoundTrips = 2 },
        });

        policy.RecordRoundTrip("a", "b", false);
        policy.RecordRoundTrip("a", "b", true);
        policy.RecordRoundTrip("a", "b", false);

        Assert.False(policy.IsNoProgress("a", "b"));
    }
}
