using System.Threading.Channels;
using WorkAgents.Core.Abstractions;

namespace WorkAgents.Infrastructure.Queue;

/// <summary>Localプロファイル用のプロセス内runキュー。</summary>
public sealed class ChannelRunQueue : IRunQueue
{
    private readonly Channel<string> _channel;

    public ChannelRunQueue(int capacity = 100)
    {
        if (capacity < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity), capacity, "Queue capacity must be positive.");
        }

        _channel = Channel.CreateBounded<string>(new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = false,
            SingleWriter = false,
        });
    }

    public ValueTask EnqueueAsync(string runId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        return _channel.Writer.WriteAsync(runId, ct);
    }

    public IAsyncEnumerable<string> ReadAllAsync(CancellationToken ct = default)
    {
        return _channel.Reader.ReadAllAsync(ct);
    }
}