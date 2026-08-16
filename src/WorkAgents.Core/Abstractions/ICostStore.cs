using WorkAgents.Core;

namespace WorkAgents.Core.Abstractions;

/// <summary>トークン使用量記録の永続化(5.10)。Local: SQLite。Azure: Cosmos DB。</summary>
public interface ICostStore
{
    Task RecordAsync(CostRecord record, CancellationToken ct = default);

    /// <summary><paramref name="since"/>以降に記録された全レコードを返す(日次/エージェント別集計はWebUI側で行う)。</summary>
    Task<IReadOnlyList<CostRecord>> ListAsync(DateTimeOffset since, CancellationToken ct = default);
}
