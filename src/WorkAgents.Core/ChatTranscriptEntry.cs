namespace WorkAgents.Core;

/// <summary>
/// Web チャット(<c>/chat</c>)の表示用会話ログ1件。MAF の <c>AgentSession</c> 内部状態とは独立に保持し、
/// ブラウザ再読み込みをまたいでスレッドの会話履歴を再表示するために使う(実際の会話継続性は
/// 既存の <see cref="Abstractions.ISessionStore"/> が担う)。
/// </summary>
public sealed record ChatTranscriptEntry
{
    public required string ThreadId { get; init; }
    public required string AgentName { get; init; }

    /// <summary>"user" または "agent"。</summary>
    public required string Role { get; init; }

    public required string Content { get; init; }
    public bool IsError { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}
