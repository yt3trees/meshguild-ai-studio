using WorkAgents.Orchestration.Teams;

namespace WorkAgents.UnitTests.Teams;

public sealed class WaitGraphTests
{
    [Fact]
    public void Register_DetectsTwoNodeCycleBeforeWaiting()
    {
        var graph = new WaitGraph();

        Assert.True(graph.Register("a", "b").Accepted);
        var result = graph.Register("b", "a");

        Assert.True(result.CycleDetected);
        Assert.Equal(new[] { "b", "a", "b" }, result.Cycle);
        Assert.False(graph.HasCycle());
    }

    [Fact]
    public void Register_DetectsThreeNodeCycle()
    {
        var graph = new WaitGraph();
        graph.Register("a", "b");
        graph.Register("b", "c");

        var result = graph.Register("c", "a");

        Assert.True(result.CycleDetected);
        Assert.Equal(new[] { "c", "a", "b", "c" }, result.Cycle);
    }

    [Fact]
    public void Remove_AllowsAPreviouslyBlockedDependency()
    {
        var graph = new WaitGraph();
        graph.Register("a", "b");
        graph.Remove("a");

        Assert.True(graph.Register("b", "a").Accepted);
        Assert.False(graph.HasCycle());
    }
}
