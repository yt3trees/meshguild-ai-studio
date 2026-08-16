using System.Runtime.CompilerServices;
using WorkAgents.Core.Abstractions;

namespace WorkAgents.Agents.Invocation;

/// <summary>
/// <see cref="IAgentInvoker"/> の実装 (T035)。既存 <see cref="IAgentRegistry"/> (AgentRegistry) の
/// モデル解決・セッション・Harness 構築をそのまま再利用し、1 ターン実行として公開する。
/// Orchestration 層はこれを通じてのみエージェントを呼び出し、LLM 呼び出しの詳細を知らない。
/// </summary>
public sealed class AgentInvoker : IAgentInvoker
{
    private readonly IAgentRegistry _registry;

    public AgentInvoker(IAgentRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        _registry = registry;
    }

    public async Task<AgentInvocationResult> InvokeAsync(AgentInvocation invocation, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(invocation);

        var utterance = await _registry.RunAsync(
            invocation.AgentName,
            invocation.Context,
            invocation.WorkingDirectory,
            invocation.ThreadId,
            invocation.MissionId,
            ct);

        // 現行 IAgentRegistry.RunAsync はトークン使用量を戻り値に含めない
        // (内部で ICostStore へ直接記録する)。Orchestration 層の予算判定は
        // ICostStore 側の記録を突き合わせる形で行う。
        return new AgentInvocationResult
        {
            Utterance = utterance,
            ToolCalls = Array.Empty<AgentToolCall>(),
        };
    }

    /// <summary>
    /// 途中経過を列挙しながら 1 ターンを実行する。セッション・コスト・承認の扱いは
    /// <see cref="InvokeAsync"/> と同等で、<see cref="IAgentRegistry.RunStreamingAsync"/> がそれを担う。
    /// </summary>
    public async IAsyncEnumerable<AgentInvocationUpdate> InvokeStreamingAsync(
        AgentInvocation invocation,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(invocation);

        await foreach (var update in _registry.RunStreamingAsync(
            invocation.AgentName,
            invocation.Context,
            invocation.WorkingDirectory,
            invocation.ThreadId,
            invocation.MissionId,
            ct))
        {
            yield return update;
        }
    }
}
