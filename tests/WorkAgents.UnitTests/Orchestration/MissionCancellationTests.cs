using WorkAgents.Core.Missions;
using WorkAgents.Infrastructure.Execution;

namespace WorkAgents.UnitTests.Orchestration;

public sealed class MissionCancellationTests
{
    [Fact]
    public void CancellationRegistry_UsesIndependentTokensPerMission()
    {
        using var registry = new InMemoryMissionCancellationRegistry();
        var first = registry.Register("mission-1");
        var second = registry.Register("mission-2");

        registry.TryCancel("mission-1");

        Assert.True(first.IsCancellationRequested);
        Assert.False(second.IsCancellationRequested);
        Assert.Equal(MissionStatus.Running, MissionStatus.Running);
    }
}
