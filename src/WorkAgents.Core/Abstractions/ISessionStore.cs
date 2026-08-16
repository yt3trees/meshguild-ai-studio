using WorkAgents.Core;

namespace WorkAgents.Core.Abstractions;

/// <summary>
/// セッション(会話)・working memory の永続化(5.9)。
/// Local: SQLite。Azure: Cosmos DB。
/// </summary>
public interface ISessionStore
{
    Task SaveAsync(SessionRecord session, CancellationToken ct = default);

    Task<SessionRecord?> LoadAsync(string threadId, CancellationToken ct = default);
}