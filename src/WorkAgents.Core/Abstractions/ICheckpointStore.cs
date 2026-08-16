using WorkAgents.Core.Missions;

namespace WorkAgents.Core.Abstractions;

/// <summary>Checkpoint の永続化抽象 (checkpoints テーブル)。</summary>
public interface ICheckpointStore
{
    Task CreateAsync(Checkpoint checkpoint, CancellationToken ct = default);

    Task<Checkpoint?> GetLatestAsync(string missionId, CancellationToken ct = default);

    Task<IReadOnlyList<Checkpoint>> ListAsync(string missionId, CancellationToken ct = default);
}
