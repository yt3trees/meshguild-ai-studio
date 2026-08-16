using WorkAgents.Core.Missions;

namespace WorkAgents.Core.Abstractions;

/// <summary>絞り込み条件付きのミッション一覧クエリ (FR-044)。</summary>
public sealed record MissionQuery
{
    public IReadOnlyList<MissionOutcome>? Outcomes { get; init; }

    public IReadOnlyList<MissionStatus>? Statuses { get; init; }

    public string? TeamName { get; init; }

    public DateTimeOffset? From { get; init; }

    public DateTimeOffset? To { get; init; }

    public int Limit { get; init; } = 50;

    public int Offset { get; init; }
}

/// <summary>Mission の永続化抽象 (missions / budgets テーブル)。</summary>
public interface IMissionStore
{
    Task CreateAsync(Mission mission, CancellationToken ct = default);

    Task<Mission?> GetAsync(string missionId, CancellationToken ct = default);

    Task<IReadOnlyList<Mission>> ListAsync(MissionQuery? query = null, CancellationToken ct = default);

    /// <summary><see cref="MissionStatusMachine"/> を経由して状態を更新する。不正遷移は例外を送出する。</summary>
    Task SetStatusAsync(
        string missionId,
        MissionStatus status,
        MissionOutcome? outcome = null,
        MissionStopReason? stopReason = null,
        string? error = null,
        CancellationToken ct = default);

    Task SetQueuePositionAsync(string missionId, MissionQueuedReason? reason, int? position, CancellationToken ct = default);

    Task UpsertBudgetAsync(Budget budget, CancellationToken ct = default);

    Task<Budget?> GetBudgetAsync(string missionId, CancellationToken ct = default);
}
