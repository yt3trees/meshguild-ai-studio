using System.Collections.Concurrent;
using WorkAgents.Core.Abstractions;

namespace WorkAgents.UnitTests.Fakes;

/// <summary>受け取った途中経過をそのまま記録する偽 <see cref="IAgentStreamSink"/>。</summary>
public sealed class RecordingAgentStreamSink : IAgentStreamSink
{
    private readonly ConcurrentQueue<AgentStreamStarted> _started = new();
    private readonly ConcurrentQueue<AgentStreamDelta> _deltas = new();
    private readonly ConcurrentQueue<AgentStreamCompleted> _completed = new();

    public IReadOnlyList<AgentStreamStarted> Started => _started.ToArray();

    public IReadOnlyList<AgentStreamDelta> Deltas => _deltas.ToArray();

    public IReadOnlyList<AgentStreamCompleted> Completed => _completed.ToArray();

    /// <summary>あるストリームで受け取った増分を連結した本文。</summary>
    public string TextOf(string streamId)
        => string.Concat(_deltas
            .Where(delta => string.Equals(delta.StreamId, streamId, StringComparison.Ordinal))
            .OrderBy(delta => delta.SeqInStream)
            .Select(delta => delta.TextDelta));

    public Task StartedAsync(AgentStreamStarted started, CancellationToken ct = default)
    {
        _started.Enqueue(started);
        return Task.CompletedTask;
    }

    public Task DeltaAsync(AgentStreamDelta delta, CancellationToken ct = default)
    {
        _deltas.Enqueue(delta);
        return Task.CompletedTask;
    }

    public Task CompletedAsync(AgentStreamCompleted completed, CancellationToken ct = default)
    {
        _completed.Enqueue(completed);
        return Task.CompletedTask;
    }
}
