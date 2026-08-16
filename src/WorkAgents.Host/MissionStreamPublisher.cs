using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;
using WorkAgents.Core.Abstractions;

namespace WorkAgents.Host;

/// <summary>
/// エージェント応答の途中経過をミッションの SignalR グループへ配信する (<see cref="IAgentStreamSink"/> の Host 実装)。
/// 配信は最善努力で、途中経過は永続化しない。確定した発言は <see cref="MissionHubPublisher"/> が
/// <c>MessageAppended</c> として別に配信する。
/// Blazor Server の再描画コストを抑えるため、増分は <see cref="FlushInterval"/> または
/// <see cref="FlushCharThreshold"/> のいずれか早い方でまとめて送る。
/// </summary>
public sealed class MissionStreamPublisher : IAgentStreamSink
{
    private static readonly TimeSpan FlushInterval = TimeSpan.FromMilliseconds(80);
    private const int FlushCharThreshold = 200;

    private readonly IHubContext<MissionHub> _hub;
    private readonly TimeProvider _time;
    private readonly ConcurrentDictionary<string, AgentStreamDeltaBuffer> _buffers = new(StringComparer.Ordinal);

    public MissionStreamPublisher(IHubContext<MissionHub> hub, TimeProvider? time = null)
    {
        ArgumentNullException.ThrowIfNull(hub);
        _hub = hub;
        _time = time ?? TimeProvider.System;
    }

    public Task StartedAsync(AgentStreamStarted started, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(started);
        _buffers[started.StreamId] = CreateBuffer();

        return _hub.Clients
            .Group(MissionHub.GroupName(started.MissionId))
            .SendAsync("MessageStreamStarted", started, ct);
    }

    public async Task DeltaAsync(AgentStreamDelta delta, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(delta);
        if (string.IsNullOrEmpty(delta.TextDelta))
        {
            return;
        }

        var buffer = _buffers.GetOrAdd(delta.StreamId, _ => CreateBuffer());
        var payload = buffer.Append(delta.TextDelta, _time.GetUtcNow());
        if (payload is null)
        {
            return;
        }

        await SendDeltaAsync(delta.MissionId, delta.StreamId, payload.Value.Seq, payload.Value.Text, ct);
    }

    public async Task CompletedAsync(AgentStreamCompleted completed, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(completed);

        if (_buffers.TryRemove(completed.StreamId, out var buffer))
        {
            var payload = buffer.Drain();
            if (payload is not null)
            {
                await SendDeltaAsync(completed.MissionId, completed.StreamId, payload.Value.Seq, payload.Value.Text, ct);
            }
        }

        await _hub.Clients
            .Group(MissionHub.GroupName(completed.MissionId))
            .SendAsync("MessageStreamCompleted", completed, ct);
    }

    private Task SendDeltaAsync(string missionId, string streamId, long seq, string text, CancellationToken ct)
        => _hub.Clients
            .Group(MissionHub.GroupName(missionId))
            .SendAsync(
                "MessageDelta",
                new AgentStreamDelta
                {
                    MissionId = missionId,
                    StreamId = streamId,
                    SeqInStream = seq,
                    TextDelta = text,
                },
                ct);

    private AgentStreamDeltaBuffer CreateBuffer()
        => new(_time.GetUtcNow(), FlushInterval, FlushCharThreshold);
}
