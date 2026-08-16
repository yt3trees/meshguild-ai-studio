using System.Runtime.CompilerServices;

namespace WorkAgents.Core.Abstractions;

/// <summary>単一ターンでのツール呼び出し 1 件 (エージェント実行の出力)。</summary>
public sealed record AgentToolCall
{
    public required string ToolName { get; init; }

    public string? ArgsSummary { get; init; }

    public string? ResultSummary { get; init; }
}

/// <summary>単一ターンのエージェント実行への入力。</summary>
public sealed record AgentInvocation
{
    /// <summary>実行するエージェント名 (agents/&lt;name&gt;)。</summary>
    public required string AgentName { get; init; }

    /// <summary>組み立て済みのコンテキスト(会話履歴・割り込み・要約を含む)。</summary>
    public required string Context { get; init; }

    /// <summary>作業ディレクトリ。無ければ null。</summary>
    public string? WorkingDirectory { get; init; }

    /// <summary>公開するツール名の集合。null のとき既定のツール集合を使う。</summary>
    public IReadOnlyList<string>? ExposedTools { get; init; }

    /// <summary>セッション継続に使うスレッド識別子。</summary>
    public string? ThreadId { get; init; }

    /// <summary>コスト計上・追跡に使うミッション ID。</summary>
    public string? MissionId { get; init; }
}

/// <summary>単一ターンのエージェント実行結果。</summary>
public sealed record AgentInvocationResult
{
    /// <summary>エージェントの発言本文。</summary>
    public required string Utterance { get; init; }

    public IReadOnlyList<AgentToolCall> ToolCalls { get; init; } = Array.Empty<AgentToolCall>();

    public long? InputTokens { get; init; }

    public long? OutputTokens { get; init; }

    public long? TotalTokens { get; init; }

    public string? ModelName { get; init; }
}

/// <summary>
/// 1 ターン実行の途中経過。<see cref="IAgentInvoker.InvokeStreamingAsync"/> が列挙する。
/// 途中経過は永続化せず、最善努力で配信するだけの一時的な値である
/// (確定した発言は従来どおり <c>MessageBus</c> だけが書き込む)。
/// </summary>
public abstract record AgentInvocationUpdate;

/// <summary>発言本文の増分。</summary>
public sealed record AgentTextDeltaUpdate(string Text) : AgentInvocationUpdate;

/// <summary>ツール呼び出しの発生を伝える。</summary>
public sealed record AgentToolCallUpdate(AgentToolCall Call) : AgentInvocationUpdate;

/// <summary>
/// HITL 承認待ちに入ったことを伝える。ストリームはこの直後に終わり、
/// 承認後の再開は従来の一括経路が担う。
/// </summary>
public sealed record AgentApprovalRequiredUpdate : AgentInvocationUpdate;

/// <summary>1 ターンの確定結果。ストリームの最後に必ず 1 回だけ現れる。</summary>
public sealed record AgentCompletedUpdate(AgentInvocationResult Result) : AgentInvocationUpdate;

/// <summary>
/// エージェント 1 ターン実行の境界 (T021)。
/// <see cref="WorkAgents.Agents"/> の実装 (AgentRegistry ベース) がこれを実装し、
/// Orchestration 層は LLM 呼び出しの詳細を知らずに 1 ターンを実行できる。
/// テストでは <c>ScriptedAgentInvoker</c> のような決定的な偽実装に差し替える。
/// </summary>
public interface IAgentInvoker
{
    Task<AgentInvocationResult> InvokeAsync(AgentInvocation invocation, CancellationToken ct = default);

    /// <summary>
    /// 途中経過を列挙しながら 1 ターンを実行する。
    /// 既定実装は <see cref="InvokeAsync"/> の結果を <see cref="AgentCompletedUpdate"/> として
    /// 1 回だけ返すため、ストリーミング非対応の実装 (テストの偽実装を含む) も無改修で動く。
    /// </summary>
    async IAsyncEnumerable<AgentInvocationUpdate> InvokeStreamingAsync(
        AgentInvocation invocation,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var result = await InvokeAsync(invocation, ct);
        yield return new AgentCompletedUpdate(result);
    }
}
