using WorkAgents.Core.Missions;

namespace WorkAgents.UnitTests.Orchestration;

public class AgentInstanceStateMachineTests
{
    [Theory]
    [InlineData(AgentInstanceState.Idle, AgentInstanceState.Thinking)]
    [InlineData(AgentInstanceState.Idle, AgentInstanceState.Stopped)]
    [InlineData(AgentInstanceState.Thinking, AgentInstanceState.ToolRunning)]
    [InlineData(AgentInstanceState.Thinking, AgentInstanceState.AwaitingApproval)]
    [InlineData(AgentInstanceState.Thinking, AgentInstanceState.AwaitingReply)]
    [InlineData(AgentInstanceState.Thinking, AgentInstanceState.Completed)]
    [InlineData(AgentInstanceState.Thinking, AgentInstanceState.Failed)]
    [InlineData(AgentInstanceState.ToolRunning, AgentInstanceState.Thinking)]
    [InlineData(AgentInstanceState.ToolRunning, AgentInstanceState.AwaitingApproval)]
    [InlineData(AgentInstanceState.AwaitingApproval, AgentInstanceState.ToolRunning)]
    [InlineData(AgentInstanceState.AwaitingApproval, AgentInstanceState.Thinking)]
    [InlineData(AgentInstanceState.AwaitingReply, AgentInstanceState.Thinking)]
    public void CanTransition_AllowsDocumentedTransitions(AgentInstanceState from, AgentInstanceState to)
    {
        Assert.True(AgentInstanceStateMachine.CanTransition(from, to));
    }

    [Theory]
    [InlineData(AgentInstanceState.Idle, AgentInstanceState.Completed)]
    [InlineData(AgentInstanceState.AwaitingReply, AgentInstanceState.ToolRunning)]
    [InlineData(AgentInstanceState.ToolRunning, AgentInstanceState.AwaitingReply)]
    public void CanTransition_RejectsUndocumentedTransitions(AgentInstanceState from, AgentInstanceState to)
    {
        Assert.False(AgentInstanceStateMachine.CanTransition(from, to));
    }

    [Theory]
    [InlineData(AgentInstanceState.Completed)]
    [InlineData(AgentInstanceState.Failed)]
    [InlineData(AgentInstanceState.Stopped)]
    public void CanTransition_TerminalStatesRejectAnyTransition(AgentInstanceState terminal)
    {
        foreach (AgentInstanceState to in Enum.GetValues<AgentInstanceState>())
        {
            Assert.False(AgentInstanceStateMachine.CanTransition(terminal, to));
        }
    }

    [Fact]
    public void EnsureTransition_ThrowsOnInvalidTransition()
    {
        Assert.Throws<InvalidOperationException>(
            () => AgentInstanceStateMachine.EnsureTransition(AgentInstanceState.Completed, AgentInstanceState.Idle));
    }
}
