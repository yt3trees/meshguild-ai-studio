using WorkAgents.Core;

namespace WorkAgents.Core.Abstractions;

/// <summary>スケジュール定義の永続化抽象(5.13.2)。Local=SQLite、Azure 移行時は Cosmos。</summary>
public interface IScheduleStore
{
    Task<IReadOnlyList<ScheduleDefinition>> ListAsync(CancellationToken ct = default);

    Task<ScheduleDefinition?> GetAsync(string name, CancellationToken ct = default);

    /// <summary>挿入または更新(name PK)。</summary>
    Task UpsertAsync(ScheduleDefinition definition, CancellationToken ct = default);

    Task DeleteAsync(string name, CancellationToken ct = default);

    /// <summary>now 時点で実行対象(enabled=1 かつ next_run_at &lt;= now)の一覧。</summary>
    Task<IReadOnlyList<ScheduleDefinition>> ListDueAsync(DateTimeOffset now, CancellationToken ct = default);

    /// <summary>実行後に last_run_at と next_run_at を更新する。</summary>
    Task UpdateAfterFireAsync(
        string name,
        DateTimeOffset lastRunAt,
        DateTimeOffset nextRunAt,
        CancellationToken ct = default);
}