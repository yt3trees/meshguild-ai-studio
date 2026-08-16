namespace WorkAgents.Core.Abstractions;

/// <summary>
/// Run 状態・トークン・コストを永続化(5.6, 5.10)。
/// Local: SQLite。Azure: Cosmos DB。
/// </summary>
public interface IRunStore
{
    Task CreateAsync(string runId, CancellationToken ct = default);

    Task CreateAsync(RunRecord run, CancellationToken ct = default);

    Task<RunRecord?> GetAsync(string runId, CancellationToken ct = default);

    Task<IReadOnlyList<RunRecord>> ListAsync(CancellationToken ct = default);

    Task<RunStatus?> GetStatusAsync(string runId, CancellationToken ct = default);

    Task SetStatusAsync(string runId, RunStatus status, CancellationToken ct = default);

    Task<bool> TrySetStatusAsync(
        string runId,
        RunStatus expectedStatus,
        RunStatus status,
        CancellationToken ct = default);

    Task CompleteAsync(
        string runId,
        RunStatus status,
        string? result = null,
        string? error = null,
        CancellationToken ct = default);
}

/// <summary>永続化済みRunを実際のエージェント実行へ渡す境界。</summary>
public interface IRunExecutor
{
    Task<string> ExecuteAsync(RunRecord run, CancellationToken ct = default);
}

/// <summary>Run状態変更をUIなどへ通知する境界。</summary>
public interface IRunProgressPublisher
{
    Task PublishAsync(RunRecord run, CancellationToken ct = default);
}