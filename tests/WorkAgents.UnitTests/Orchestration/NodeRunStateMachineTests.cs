using WorkAgents.Core.Graphs;

namespace WorkAgents.UnitTests.Orchestration;

public class NodeRunStateMachineTests
{
    [Theory]
    [InlineData(NodeRunState.Pending, NodeRunState.Running)]
    [InlineData(NodeRunState.Pending, NodeRunState.Skipped)]
    [InlineData(NodeRunState.Pending, NodeRunState.Unreached)]
    [InlineData(NodeRunState.Running, NodeRunState.Waiting)]
    [InlineData(NodeRunState.Running, NodeRunState.Succeeded)]
    [InlineData(NodeRunState.Running, NodeRunState.Failed)]
    [InlineData(NodeRunState.Waiting, NodeRunState.Running)]
    [InlineData(NodeRunState.Waiting, NodeRunState.Succeeded)]
    [InlineData(NodeRunState.Waiting, NodeRunState.Failed)]
    public void CanTransition_AllowsDocumentedTransitions(NodeRunState from, NodeRunState to)
    {
        Assert.True(NodeRunStateMachine.CanTransition(from, to));
    }

    [Theory]
    [InlineData(NodeRunState.Pending, NodeRunState.Succeeded)]
    [InlineData(NodeRunState.Pending, NodeRunState.Waiting)]
    public void CanTransition_RejectsUndocumentedTransitions(NodeRunState from, NodeRunState to)
    {
        Assert.False(NodeRunStateMachine.CanTransition(from, to));
    }

    [Theory]
    [InlineData(NodeRunState.Succeeded)]
    [InlineData(NodeRunState.Failed)]
    [InlineData(NodeRunState.Skipped)]
    [InlineData(NodeRunState.Unreached)]
    public void CanTransition_TerminalStatesRejectAnyTransition(NodeRunState terminal)
    {
        foreach (NodeRunState to in Enum.GetValues<NodeRunState>())
        {
            Assert.False(NodeRunStateMachine.CanTransition(terminal, to));
        }
    }

    [Fact]
    public void EnsureTransition_ThrowsOnInvalidTransition()
    {
        Assert.Throws<InvalidOperationException>(
            () => NodeRunStateMachine.EnsureTransition(NodeRunState.Succeeded, NodeRunState.Running));
    }
}
