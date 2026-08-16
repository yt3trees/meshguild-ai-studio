using WorkAgents.Core.Missions;
using WorkAgents.Core.Teams;
using WorkAgents.Orchestration.Teams;

namespace WorkAgents.UnitTests.Teams;

public sealed class ConversationPolicyTests
{
    [Fact]
    public void Check_RejectsDirectMessageWhenChannelIsNotAllowed()
    {
        var policy = new ConversationPolicy(CreateTeam());

        var result = policy.Check("dev-agent", "test-agent", MessageKind.Question);

        Assert.False(result.Allowed);
        Assert.Equal("channel_not_allowed", result.Code);
    }

    [Fact]
    public void Check_AllowsDeclaredDirectChannel()
    {
        var policy = new ConversationPolicy(CreateTeam());

        var result = policy.Check("dev-agent", "spec-agent", MessageKind.Question);

        Assert.True(result.Allowed);
    }

    [Fact]
    public void Check_AllowsUndeclaredDirectMessageWhenDefaultIsDirect()
    {
        var policy = new ConversationPolicy(CreateTeam(channelsDefault: ChannelDefault.Direct));

        var result = policy.Check("dev-agent", "test-agent", MessageKind.Question);

        Assert.True(result.Allowed);
    }

    [Fact]
    public void Check_RejectsDelegationPastConfiguredDepth()
    {
        var policy = new ConversationPolicy(CreateTeam(maxDepth: 2));

        var result = policy.Check("orchestrator-agent", "dev-agent", MessageKind.Delegate, delegationDepth: 3);

        Assert.False(result.Allowed);
        Assert.Equal("delegation_depth_exceeded", result.Code);
    }

    [Fact]
    public void RecordRoundTrip_StopsAfterRepeatedNonProgress()
    {
        var policy = new ConversationPolicy(CreateTeam(noProgress: 2));

        Assert.True(policy.RecordRoundTrip("dev-agent", "spec-agent", madeProgress: false));
        Assert.True(policy.RecordRoundTrip("dev-agent", "spec-agent", madeProgress: false));
        Assert.False(policy.RecordRoundTrip("dev-agent", "spec-agent", madeProgress: false));
        Assert.True(policy.IsNoProgress("dev-agent", "spec-agent"));
    }

    private static TeamDefinition CreateTeam(
        int maxDepth = 3,
        int noProgress = 5,
        ChannelDefault channelsDefault = ChannelDefault.ViaOrchestrator)
        => new()
        {
            Name = "test-team",
            Orchestrator = new TeamOrchestrator { Agent = "orchestrator-agent" },
            Members =
            [
                new TeamMember { Agent = "dev-agent" },
                new TeamMember { Agent = "test-agent" },
                new TeamMember { Agent = "spec-agent" },
            ],
            ChannelsDefault = channelsDefault,
            ChannelsAllow =
            [
                new ChannelRule
                {
                    From = "dev-agent",
                    To = "spec-agent",
                    Kinds = [MessageKind.Question, MessageKind.Answer],
                },
            ],
            Limits = new TeamLimits { MaxDelegationDepth = maxDepth, NoProgressRoundTrips = noProgress },
        };
}
