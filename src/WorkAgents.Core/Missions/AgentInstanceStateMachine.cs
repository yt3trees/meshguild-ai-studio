namespace WorkAgents.Core.Missions;

/// <summary>AgentInstance 状態機械で許可される遷移を一元化する (data-model.md、既存 RunStatusMachine と同じ方式)。</summary>
public static class AgentInstanceStateMachine
{
    public static bool CanTransition(AgentInstanceState from, AgentInstanceState to)
    {
        return from switch
        {
            AgentInstanceState.Idle => to is AgentInstanceState.Thinking or AgentInstanceState.Stopped,
            AgentInstanceState.Thinking => to is AgentInstanceState.ToolRunning
                or AgentInstanceState.AwaitingApproval
                or AgentInstanceState.AwaitingReply
                or AgentInstanceState.Completed
                or AgentInstanceState.Failed
                or AgentInstanceState.Idle
                or AgentInstanceState.Stopped,
            AgentInstanceState.ToolRunning => to is AgentInstanceState.Thinking
                or AgentInstanceState.AwaitingApproval
                or AgentInstanceState.Idle
                or AgentInstanceState.Failed
                or AgentInstanceState.Stopped,
            AgentInstanceState.AwaitingApproval => to is AgentInstanceState.ToolRunning
                or AgentInstanceState.Thinking
                or AgentInstanceState.Failed
                or AgentInstanceState.Stopped,
            AgentInstanceState.AwaitingReply => to is AgentInstanceState.Thinking
                or AgentInstanceState.Failed
                or AgentInstanceState.Stopped,
            AgentInstanceState.Completed
                or AgentInstanceState.Failed
                or AgentInstanceState.Stopped => false,
            _ => false,
        };
    }

    public static void EnsureTransition(AgentInstanceState from, AgentInstanceState to)
    {
        if (!CanTransition(from, to))
        {
            throw new InvalidOperationException($"Invalid agent instance state transition: {from} -> {to}.");
        }
    }
}
