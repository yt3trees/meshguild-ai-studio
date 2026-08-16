using WorkAgents.Core;

namespace WorkAgents.Core.Abstractions;

/// <summary>Web チャットの実行トレース(run単位のメタデータ)の永続化。Local: SQLite。Azure: Cosmos DB。</summary>
public interface IChatTraceStore
{
    Task AppendAsync(ChatTraceEntry entry, CancellationToken ct = default);

    Task<IReadOnlyList<ChatTraceEntry>> ListAsync(string threadId, CancellationToken ct = default);
}
