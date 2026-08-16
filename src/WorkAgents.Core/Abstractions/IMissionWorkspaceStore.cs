using WorkAgents.Core.Missions;

namespace WorkAgents.Core.Abstractions;

public interface IMissionWorkspaceStore
{
    Task<MissionWorkspaceRecord?> GetAsync(string missionId, CancellationToken ct = default);

    Task RecordPreparedAsync(MissionWorkspaceRecord record, CancellationToken ct = default);

    Task MarkDeletedAsync(string missionId, DateTimeOffset deletedAtUtc, CancellationToken ct = default);
}
