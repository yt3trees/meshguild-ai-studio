using WorkAgents.Core.Missions;

namespace WorkAgents.Core.Abstractions;

/// <summary>Message / ConversationSummary の永続化抽象 (messages / conversation_summaries テーブル)。</summary>
public interface IMessageStore
{
    /// <summary>採番して永続化する。返却される Message の Seq が確定した値。</summary>
    Task<Message> AppendAsync(Message message, CancellationToken ct = default);

    Task<Message?> GetAsync(string messageId, CancellationToken ct = default);

    Task<IReadOnlyList<Message>> ListAsync(
        string missionId,
        long sinceSeq = 0,
        string? threadKey = null,
        bool includeDiscarded = false,
        int limit = 500,
        CancellationToken ct = default);

    Task DiscardAsync(string missionId, long afterSeq, string checkpointId, CancellationToken ct = default);

    Task AddSummaryAsync(ConversationSummary summary, CancellationToken ct = default);

    Task<IReadOnlyList<ConversationSummary>> ListSummariesAsync(
        string missionId,
        string? threadKey = null,
        CancellationToken ct = default);
}
