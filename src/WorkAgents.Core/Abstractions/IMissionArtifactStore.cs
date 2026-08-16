using WorkAgents.Core.Missions;

namespace WorkAgents.Core.Abstractions;

public interface IMissionArtifactStore : IArtifactStore
{
    Task SaveMissionArtifactAsync(MissionArtifact artifact, CancellationToken ct = default);

    Task<IReadOnlyList<MissionArtifact>> ListMissionAsync(string missionId, bool includeDiscarded = false, CancellationToken ct = default);
}
