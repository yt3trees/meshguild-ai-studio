using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using WorkAgents.Core.Abstractions;

namespace WorkAgents.UnitTests.Fakes;

/// <summary>
/// 決定的な偽 <see cref="IAgentInvoker"/> (T036)。LLM へ接続せず、あらかじめ登録した
/// 台本 (発言・ツール呼び出し) をエージェント名ごとに順番に返す。台本を使い切ると例外を送出する。
/// </summary>
public sealed class ScriptedAgentInvoker : IAgentInvoker
{
    private readonly ConcurrentDictionary<string, Queue<AgentInvocationResult>> _scripts = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentQueue<AgentInvocation> _invocations = new();

    /// <summary>指定エージェントが呼ばれたときに順番に返す結果を登録する。</summary>
    public ScriptedAgentInvoker Script(string agentName, params AgentInvocationResult[] results)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentName);
        var queue = _scripts.GetOrAdd(agentName, static _ => new Queue<AgentInvocationResult>());
        foreach (var result in results)
        {
            queue.Enqueue(result);
        }
        return this;
    }

    /// <summary>単純な発言のみの結果を台本へ追加する。</summary>
    public ScriptedAgentInvoker Script(string agentName, string utterance)
        => Script(agentName, new AgentInvocationResult { Utterance = utterance });

    /// <summary>これまでに受け取った呼び出しの履歴 (呼び出し順)。</summary>
    public IReadOnlyList<AgentInvocation> Invocations => _invocations.ToArray();

    public Task<AgentInvocationResult> InvokeAsync(AgentInvocation invocation, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(invocation);
        _invocations.Enqueue(invocation);

        if (!_scripts.TryGetValue(invocation.AgentName, out var queue) || queue.Count == 0)
        {
            throw new InvalidOperationException(
                $"ScriptedAgentInvoker has no scripted result left for agent '{invocation.AgentName}'.");
        }

        var result = queue.Dequeue();
        return Task.FromResult(result);
    }

    /// <summary>台本の発言を <see cref="ChunkSize"/> 文字ずつに割って流す。</summary>
    public int ChunkSize { get; set; } = 8;

    public async IAsyncEnumerable<AgentInvocationUpdate> InvokeStreamingAsync(
        AgentInvocation invocation,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var result = await InvokeAsync(invocation, ct);
        for (var offset = 0; offset < result.Utterance.Length; offset += ChunkSize)
        {
            yield return new AgentTextDeltaUpdate(
                result.Utterance.Substring(offset, Math.Min(ChunkSize, result.Utterance.Length - offset)));
        }

        yield return new AgentCompletedUpdate(result);
    }
}
