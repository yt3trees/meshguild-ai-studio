using WorkAgents.Core.Abstractions;

namespace WorkAgents.UnitTests.Teams;

public sealed class AgentStreamDeltaBufferTests
{
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Append_HoldsSmallDeltasUntilTheIntervalElapses()
    {
        var buffer = new AgentStreamDeltaBuffer(Start, TimeSpan.FromMilliseconds(80), threshold: 200);

        Assert.Null(buffer.Append("ab", Start));
        Assert.Null(buffer.Append("cd", Start.AddMilliseconds(40)));

        var flushed = buffer.Append("ef", Start.AddMilliseconds(80));
        Assert.NotNull(flushed);
        Assert.Equal(0, flushed!.Value.Seq);
        Assert.Equal("abcdef", flushed.Value.Text);
    }

    [Fact]
    public void Append_FlushesImmediatelyWhenTheThresholdIsReached()
    {
        var buffer = new AgentStreamDeltaBuffer(Start, TimeSpan.FromMinutes(1), threshold: 4);

        Assert.Null(buffer.Append("ab", Start));

        var flushed = buffer.Append("cd", Start);
        Assert.NotNull(flushed);
        Assert.Equal("abcd", flushed!.Value.Text);
    }

    [Fact]
    public void Drain_ReturnsTheRemainderAndKeepsSequenceContiguous()
    {
        var buffer = new AgentStreamDeltaBuffer(Start, TimeSpan.FromMilliseconds(80), threshold: 4);

        var first = buffer.Append("abcd", Start);
        Assert.NotNull(first);
        Assert.Equal(0, first!.Value.Seq);

        Assert.Null(buffer.Append("ef", Start));

        var drained = buffer.Drain();
        Assert.NotNull(drained);
        Assert.Equal(1, drained!.Value.Seq);
        Assert.Equal("ef", drained.Value.Text);

        // 残りが無ければ何も返さない (完了通知だけを送る)。
        Assert.Null(buffer.Drain());
    }
}
