using WorkAgents.Core.Missions;

namespace WorkAgents.UnitTests.Orchestration;

public class MissionStatusMachineTests
{
    [Theory]
    [InlineData(MissionStatus.Queued, MissionStatus.Running)]
    [InlineData(MissionStatus.Queued, MissionStatus.Aborted)]
    [InlineData(MissionStatus.Running, MissionStatus.Succeeded)]
    [InlineData(MissionStatus.Running, MissionStatus.NotConverged)]
    [InlineData(MissionStatus.Running, MissionStatus.Failed)]
    [InlineData(MissionStatus.Running, MissionStatus.Aborted)]
    [InlineData(MissionStatus.Running, MissionStatus.Paused)]
    [InlineData(MissionStatus.Running, MissionStatus.AwaitingApproval)]
    [InlineData(MissionStatus.Paused, MissionStatus.Running)]
    [InlineData(MissionStatus.Paused, MissionStatus.Aborted)]
    [InlineData(MissionStatus.AwaitingApproval, MissionStatus.Running)]
    [InlineData(MissionStatus.AwaitingApproval, MissionStatus.Aborted)]
    public void CanTransition_AllowsDocumentedTransitions(MissionStatus from, MissionStatus to)
    {
        Assert.True(MissionStatusMachine.CanTransition(from, to));
    }

    [Theory]
    [InlineData(MissionStatus.Queued, MissionStatus.Succeeded)]
    [InlineData(MissionStatus.Queued, MissionStatus.Paused)]
    [InlineData(MissionStatus.Queued, MissionStatus.AwaitingApproval)]
    [InlineData(MissionStatus.Paused, MissionStatus.NotConverged)]
    [InlineData(MissionStatus.Paused, MissionStatus.AwaitingApproval)]
    [InlineData(MissionStatus.AwaitingApproval, MissionStatus.Paused)]
    [InlineData(MissionStatus.AwaitingApproval, MissionStatus.Succeeded)]
    public void CanTransition_RejectsUndocumentedTransitions(MissionStatus from, MissionStatus to)
    {
        Assert.False(MissionStatusMachine.CanTransition(from, to));
    }

    [Theory]
    [InlineData(MissionStatus.Succeeded)]
    [InlineData(MissionStatus.NotConverged)]
    [InlineData(MissionStatus.Failed)]
    [InlineData(MissionStatus.Aborted)]
    public void CanTransition_TerminalStatesRejectAnyTransition(MissionStatus terminal)
    {
        foreach (MissionStatus to in Enum.GetValues<MissionStatus>())
        {
            Assert.False(MissionStatusMachine.CanTransition(terminal, to));
        }
    }

    [Fact]
    public void EnsureTransition_ThrowsOnInvalidTransition()
    {
        Assert.Throws<InvalidOperationException>(
            () => MissionStatusMachine.EnsureTransition(MissionStatus.Succeeded, MissionStatus.Running));
    }

    [Fact]
    public void EnsureTransition_DoesNotThrowOnValidTransition()
    {
        MissionStatusMachine.EnsureTransition(MissionStatus.Queued, MissionStatus.Running);
    }
}
