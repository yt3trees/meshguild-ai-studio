using WorkAgents.Core.Missions;

namespace WorkAgents.Core.Abstractions;

/// <summary>Intervention の永続化抽象 (interventions テーブル)。</summary>
public interface IInterventionStore
{
    Task CreateAsync(Intervention intervention, CancellationToken ct = default);

    Task<IReadOnlyList<Intervention>> ListUnappliedAsync(
        string missionId,
        string? targetInstanceId = null,
        CancellationToken ct = default);

    Task MarkAppliedAsync(string interventionId, string appliedToMessageId, CancellationToken ct = default);
}
