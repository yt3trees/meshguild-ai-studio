namespace WorkAgents.Core.Abstractions;

/// <summary>エージェントが発言を書き始めたことを伝える。</summary>
public sealed record AgentStreamStarted
{
    public required string MissionId { get; init; }

    /// <summary>この 1 ターンの途中経過を束ねる識別子。永続化しない。</summary>
    public required string StreamId { get; init; }

    public required string InstanceId { get; init; }

    public required string AgentName { get; init; }
}

/// <summary>発言本文の増分 1 件。</summary>
public sealed record AgentStreamDelta
{
    public required string MissionId { get; init; }

    public required string StreamId { get; init; }

    /// <summary>ストリーム内の 0 始まりの通し番号。受信側が欠落と順序逆転を検出するために使う。</summary>
    public required long SeqInStream { get; init; }

    public required string TextDelta { get; init; }
}

/// <summary>
/// 途中経過の配信が終わったことを伝える。受信側はこの時点で暫定表示を閉じてよい。
/// 確定した発言は従来どおり <c>MessageAppended</c> として別に届く。
/// </summary>
public sealed record AgentStreamCompleted
{
    public required string MissionId { get; init; }

    public required string StreamId { get; init; }

    /// <summary>承認待ちなどで途中打ち切りになった場合は true。</summary>
    public bool Interrupted { get; init; }
}

/// <summary>
/// エージェント応答の途中経過の配信口。実装は最善努力で配信するだけで、永続化しない
/// (確定した発言は <c>MessageBus</c> だけが書き込むという前提を崩さない)。
/// Host が SignalR 実装を登録し、テストと配信不要な経路は <see cref="NullAgentStreamSink"/> を使う。
/// </summary>
public interface IAgentStreamSink
{
    Task StartedAsync(AgentStreamStarted started, CancellationToken ct = default);

    Task DeltaAsync(AgentStreamDelta delta, CancellationToken ct = default);

    Task CompletedAsync(AgentStreamCompleted completed, CancellationToken ct = default);
}

/// <summary>何も配信しない実装。</summary>
public sealed class NullAgentStreamSink : IAgentStreamSink
{
    public static NullAgentStreamSink Instance { get; } = new();

    public Task StartedAsync(AgentStreamStarted started, CancellationToken ct = default) => Task.CompletedTask;

    public Task DeltaAsync(AgentStreamDelta delta, CancellationToken ct = default) => Task.CompletedTask;

    public Task CompletedAsync(AgentStreamCompleted completed, CancellationToken ct = default) => Task.CompletedTask;
}
