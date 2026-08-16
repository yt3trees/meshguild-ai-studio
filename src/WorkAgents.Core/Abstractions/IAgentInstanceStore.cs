using WorkAgents.Core.Missions;

namespace WorkAgents.Core.Abstractions;

/// <summary>AgentInstance の永続化抽象 (agent_instances テーブル)。</summary>
public interface IAgentInstanceStore
{
    Task CreateAsync(AgentInstance instance, CancellationToken ct = default);

    Task<AgentInstance?> GetAsync(string instanceId, CancellationToken ct = default);

    Task<IReadOnlyList<AgentInstance>> ListByMissionAsync(string missionId, CancellationToken ct = default);

    /// <summary><see cref="AgentInstanceStateMachine"/> を経由して状態を更新する。</summary>
    Task SetStateAsync(
        string instanceId,
        AgentInstanceState state,
        string? awaitingInstanceId = null,
        CancellationToken ct = default);

    Task SetLeftAsync(string instanceId, string leaveReason, CancellationToken ct = default);
}
