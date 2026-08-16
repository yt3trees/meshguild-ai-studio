using WorkAgents.Core;

namespace WorkAgents.Core.Abstractions;

/// <summary>
/// Web チャットの表示用会話ログの永続化。Local: SQLite。Azure: Cosmos DB。
/// </summary>
public interface IChatTranscriptStore
{
    Task AppendAsync(ChatTranscriptEntry entry, CancellationToken ct = default);

    Task<IReadOnlyList<ChatTranscriptEntry>> ListAsync(string threadId, CancellationToken ct = default);

    /// <summary>指定Agentで直近にメッセージが記録されたthreadIdを返す。無ければnull。</summary>
    Task<string?> GetLatestThreadIdAsync(string agentName, CancellationToken ct = default);
}
