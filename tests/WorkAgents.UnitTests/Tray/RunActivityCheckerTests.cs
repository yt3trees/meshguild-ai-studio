using WorkAgents.Tray;

namespace WorkAgents.UnitTests.Tray;

public class RunActivityCheckerTests
{
    [Fact]
    public async Task HasActiveRunsAsync_NoRuns_ReturnsFalse()
    {
        var checker = new RunActivityChecker((_, _) => Task.FromResult<IReadOnlyList<string>?>([]));
        Assert.False(await checker.HasActiveRunsAsync(5160));
    }

    [Theory]
    [InlineData("Queued")]
    [InlineData("Running")]
    [InlineData("AwaitingApproval")]
    public async Task HasActiveRunsAsync_ActiveStatusPresent_ReturnsTrue(string status)
    {
        var checker = new RunActivityChecker((_, _) => Task.FromResult<IReadOnlyList<string>?>([status]));
        Assert.True(await checker.HasActiveRunsAsync(5160));
    }

    [Theory]
    [InlineData("Succeeded")]
    [InlineData("Failed")]
    [InlineData("Aborted")]
    public async Task HasActiveRunsAsync_OnlyTerminalStatuses_ReturnsFalse(string status)
    {
        var checker = new RunActivityChecker((_, _) => Task.FromResult<IReadOnlyList<string>?>([status]));
        Assert.False(await checker.HasActiveRunsAsync(5160));
    }

    [Fact]
    public async Task HasActiveRunsAsync_FetchReturnsNull_FailsSafeToTrue()
    {
        // Host疎通不可(非成功ステータス)の場合、フェッチ関数はnullを返す想定。
        var checker = new RunActivityChecker((_, _) => Task.FromResult<IReadOnlyList<string>?>(null));
        Assert.True(await checker.HasActiveRunsAsync(5160));
    }

    [Fact]
    public async Task HasActiveRunsAsync_FetchThrows_FailsSafeToTrue()
    {
        var checker = new RunActivityChecker((_, _) => throw new HttpRequestException("connection refused"));
        Assert.True(await checker.HasActiveRunsAsync(5160));
    }
}
