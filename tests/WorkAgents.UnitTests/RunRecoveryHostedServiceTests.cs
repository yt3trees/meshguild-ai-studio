using Microsoft.Extensions.Logging.Abstractions;
using WorkAgents.Core;
using WorkAgents.Infrastructure.Execution;
using WorkAgents.Infrastructure.Queue;
using WorkAgents.Infrastructure.Stores;

namespace WorkAgents.UnitTests;

public sealed class RunRecoveryHostedServiceTests
{
    [Fact]
    public async Task StartAsync_ReenqueuesQueuedRuns()
    {
        var databasePath = CreateDatabasePath();
        try
        {
            var store = new SqliteRunStore(databasePath);
            var queue = new ChannelRunQueue();
            await store.CreateAsync(CreateRun("run-queued"));
            var service = new RunRecoveryHostedService(store, queue, NullLogger<RunRecoveryHostedService>.Instance);

            await service.StartAsync(CancellationToken.None);

            var reader = queue.ReadAllAsync(CancellationToken.None).GetAsyncEnumerator();
            var moved = await reader.MoveNextAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(moved);
            Assert.Equal("run-queued", reader.Current);

            var persisted = await store.GetAsync("run-queued");
            Assert.Equal(RunStatus.Queued, persisted?.Status);
        }
        finally
        {
            DeleteDatabaseDirectory(databasePath);
        }
    }

    [Fact]
    public async Task StartAsync_AbortsOrphanedRunningRuns()
    {
        var databasePath = CreateDatabasePath();
        try
        {
            var store = new SqliteRunStore(databasePath);
            var queue = new ChannelRunQueue();
            var run = CreateRun("run-running");
            await store.CreateAsync(run);
            Assert.True(await store.TrySetStatusAsync("run-running", RunStatus.Queued, RunStatus.Running));
            var service = new RunRecoveryHostedService(store, queue, NullLogger<RunRecoveryHostedService>.Instance);

            await service.StartAsync(CancellationToken.None);

            var persisted = await store.GetAsync("run-running");
            Assert.Equal(RunStatus.Aborted, persisted?.Status);
            Assert.Equal("Host restarted while this run was in progress.", persisted?.Error);
        }
        finally
        {
            DeleteDatabaseDirectory(databasePath);
        }
    }

    [Fact]
    public async Task StartAsync_LeavesTerminalRunsUntouched()
    {
        var databasePath = CreateDatabasePath();
        try
        {
            var store = new SqliteRunStore(databasePath);
            var queue = new ChannelRunQueue();
            var run = CreateRun("run-done");
            await store.CreateAsync(run);
            Assert.True(await store.TrySetStatusAsync("run-done", RunStatus.Queued, RunStatus.Running));
            await store.CompleteAsync("run-done", RunStatus.Succeeded, result: "ok");
            var service = new RunRecoveryHostedService(store, queue, NullLogger<RunRecoveryHostedService>.Instance);

            await service.StartAsync(CancellationToken.None);

            var persisted = await store.GetAsync("run-done");
            Assert.Equal(RunStatus.Succeeded, persisted?.Status);
            Assert.Equal("ok", persisted?.Result);
        }
        finally
        {
            DeleteDatabaseDirectory(databasePath);
        }
    }

    private static RunRecord CreateRun(string runId) => new()
    {
        RunId = runId,
        AgentName = "test-agent",
        UserMessage = "test",
    };

    private static string CreateDatabasePath()
        => Path.Combine(Path.GetTempPath(), "work-agents-tests", $"{Guid.NewGuid():N}", "runs.db");

    private static void DeleteDatabaseDirectory(string databasePath)
    {
        var directory = Path.GetDirectoryName(databasePath);
        if (directory is not null && Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
