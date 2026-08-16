namespace WorkAgents.Core.Missions;

/// <summary>Mission 状態機械で許可される遷移を一元化する (data-model.md、既存 RunStatusMachine と同じ方式)。</summary>
public static class MissionStatusMachine
{
    public static bool CanTransition(MissionStatus from, MissionStatus to)
    {
        return from switch
        {
            MissionStatus.Queued => to is MissionStatus.Running or MissionStatus.Aborted,
            MissionStatus.Running => to is MissionStatus.Succeeded
                or MissionStatus.NotConverged
                or MissionStatus.Failed
                or MissionStatus.Aborted
                or MissionStatus.Paused
                or MissionStatus.AwaitingApproval,
            MissionStatus.Paused => to is MissionStatus.Running or MissionStatus.Aborted,
            MissionStatus.AwaitingApproval => to is MissionStatus.Running or MissionStatus.Aborted,
            MissionStatus.Succeeded
                or MissionStatus.NotConverged
                or MissionStatus.Failed
                or MissionStatus.Aborted => false,
            _ => false,
        };
    }

    public static void EnsureTransition(MissionStatus from, MissionStatus to)
    {
        if (!CanTransition(from, to))
        {
            throw new InvalidOperationException($"Invalid mission status transition: {from} -> {to}.");
        }
    }
}
