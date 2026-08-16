namespace WorkAgents.Core.Graphs;

/// <summary>NodeRun 状態機械で許可される遷移を一元化する (data-model.md、既存 RunStatusMachine と同じ方式)。</summary>
public static class NodeRunStateMachine
{
    public static bool CanTransition(NodeRunState from, NodeRunState to)
    {
        return from switch
        {
            NodeRunState.Pending => to is NodeRunState.Running
                or NodeRunState.Skipped
                or NodeRunState.Unreached,
            NodeRunState.Running => to is NodeRunState.Waiting
                or NodeRunState.Succeeded
                or NodeRunState.Failed,
            NodeRunState.Waiting => to is NodeRunState.Running
                or NodeRunState.Succeeded
                or NodeRunState.Failed,
            NodeRunState.Succeeded
                or NodeRunState.Failed
                or NodeRunState.Skipped
                or NodeRunState.Unreached => false,
            _ => false,
        };
    }

    public static void EnsureTransition(NodeRunState from, NodeRunState to)
    {
        if (!CanTransition(from, to))
        {
            throw new InvalidOperationException($"Invalid node run state transition: {from} -> {to}.");
        }
    }
}
