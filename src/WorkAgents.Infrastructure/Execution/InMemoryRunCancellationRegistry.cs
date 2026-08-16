using System.Collections.Concurrent;
using WorkAgents.Core.Abstractions;

namespace WorkAgents.Infrastructure.Execution;

/// <summary>プロセス内 <see cref="ConcurrentDictionary{TKey,TValue}"/> によるRunキャンセル仲介(Local/Azure共通)。</summary>
public sealed class InMemoryRunCancellationRegistry : IRunCancellationRegistry
{
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _sources = new();
    private readonly ConcurrentDictionary<string, bool> _explicitlyCancelled = new();

    public CancellationTokenSource Register(string runId, CancellationToken linkedToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        var cts = CancellationTokenSource.CreateLinkedTokenSource(linkedToken);
        _sources[runId] = cts;
        return cts;
    }

    public bool TryCancel(string runId)
    {
        if (!_sources.TryGetValue(runId, out var cts))
        {
            return false;
        }

        try
        {
            _explicitlyCancelled[runId] = true;
            cts.Cancel();
        }
        catch (ObjectDisposedException)
        {
            return false;
        }

        return true;
    }

    public bool WasExplicitlyCancelled(string runId) => _explicitlyCancelled.ContainsKey(runId);

    public void Remove(string runId)
    {
        if (_sources.TryRemove(runId, out var cts))
        {
            cts.Dispose();
        }
        _explicitlyCancelled.TryRemove(runId, out _);
    }
}
