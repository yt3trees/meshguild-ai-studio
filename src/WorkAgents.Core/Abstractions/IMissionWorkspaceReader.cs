using WorkAgents.Core.Missions;

namespace WorkAgents.Core.Abstractions;

public interface IMissionWorkspaceReader
{
    Task<MissionWorkspaceSnapshot> ReadAsync(string missionId, CancellationToken ct = default);
}
