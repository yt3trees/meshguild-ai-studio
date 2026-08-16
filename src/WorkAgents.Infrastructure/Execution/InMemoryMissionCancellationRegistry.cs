using System.Collections.Concurrent;
using WorkAgents.Core.Abstractions;

namespace WorkAgents.Infrastructure.Execution;

public sealed class InMemoryMissionCancellationRegistry : IMissionCancellationRegistry, IDisposable
{
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _sources = new(StringComparer.Ordinal);

    public CancellationToken Register(string missionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(missionId);
        var source = _sources.GetOrAdd(missionId, _ => new CancellationTokenSource());
        return source.Token;
    }

    public bool TryCancel(string missionId)
    {
        if (!_sources.TryGetValue(missionId, out var source))
        {
            return false;
        }

        source.Cancel();
        return true;
    }

    public void Remove(string missionId)
    {
        if (_sources.TryRemove(missionId, out var source))
        {
            source.Dispose();
        }
    }

    public void Dispose()
    {
        foreach (var missionId in _sources.Keys)
        {
            Remove(missionId);
        }
    }
}
