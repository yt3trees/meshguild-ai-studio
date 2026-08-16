using System.Collections.Concurrent;
using Microsoft.Extensions.Logging.Abstractions;
using WorkAgents.Core;
using WorkAgents.Core.Abstractions;
using WorkAgents.Infrastructure.Execution;
using WorkAgents.Infrastructure.Queue;
using WorkAgents.Infrastructure.Stores;

namespace WorkAgents.UnitTests;

public sealed class RunBackgroundServiceTests
{
    [Fact]
    public async Task ProcessesDuplicateDeliveryOnlyOnce()
    {
        var databasePath = CreateDatabasePath();
        var queue = new ChannelRunQueue();
        var store = new SqliteRunStore(databasePath);
        var executor = new RecordingExecutor();
        var publisher = new RecordingProgressPublisher();
        var service = CreateService(queue, store, executor, publisher);
        var run = CreateRun("run-success");

        try
        {
            await store.CreateAsync(run);
            await service.StartAsync(CancellationToken.None);
            await queue.EnqueueAsync(run.RunId);
            await queue.EnqueueAsync(run.RunId);
            await executor.FirstExecution.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await WaitForTerminalRunAsync(store, run.RunId);
            await service.StopAsync(CancellationToken.None);

            var persisted = await store.GetAsync(run.RunId);
            Assert.NotNull(persisted);
            Assert.Equal(RunStatus.Succeeded, persisted.Status);
            Assert.Equal("completed", persisted.Result);
            Assert.Equal(1, executor.Calls);
            Assert.Contains(publisher.Updates, run => run.Status == RunStatus.Running);
            Assert.Contains(publisher.Updates, run => run.Status == RunStatus.Succeeded);
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
            DeleteDatabaseDirectory(databasePath);
        }
    }

    [Fact]
    public async Task PersistsFailedStatusWithoutPersistingExceptionMessage()
    {
        var databasePath = CreateDatabasePath();
        var queue = new ChannelRunQueue();
        var store = new SqliteRunStore(databasePath);
        var executor = new RecordingExecutor { Exception = new InvalidOperationException("secret-token") };
        var publisher = new RecordingProgressPublisher();
        var service = CreateService(queue, store, executor, publisher);
        var run = CreateRun("run-failed");

        try
        {
            await store.CreateAsync(run);
            await service.StartAsync(CancellationToken.None);
            await queue.EnqueueAsync(run.RunId);
            await executor.FirstExecution.Task.WaitAsync(TimeSpan.FromSeconds(5));

            var persisted = await WaitForTerminalRunAsync(store, run.RunId);
            Assert.NotNull(persisted);
            Assert.Equal(RunStatus.Failed, persisted.Status);
            Assert.Equal("Agent execution failed.", persisted.Error);
            Assert.DoesNotContain("secret-token", persisted.Error, StringComparison.Ordinal);
            Assert.Contains(publisher.Updates, run => run.Status == RunStatus.Failed);
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
            DeleteDatabaseDirectory(databasePath);
        }
    }

    [Fact]
    public async Task CancelRequest_AbortsRunningExecution()
    {
        var databasePath = CreateDatabasePath();
        var queue = new ChannelRunQueue();
        var store = new SqliteRunStore(databasePath);
        var registry = new InMemoryRunCancellationRegistry();
        var executor = new BlockingExecutor();
        var publisher = new RecordingProgressPublisher();
        var service = CreateService(queue, store, executor, publisher, registry);
        var run = CreateRun("run-cancelled");

        try
        {
            await store.CreateAsync(run);
            await service.StartAsync(CancellationToken.None);
            await queue.EnqueueAsync(run.RunId);
            await executor.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.True(registry.TryCancel(run.RunId));

            var persisted = await WaitForTerminalRunAsync(store, run.RunId);
            Assert.NotNull(persisted);
            Assert.Equal(RunStatus.Aborted, persisted.Status);
            Assert.Equal("Run was cancelled by request.", persisted.Error);
            await WaitForPublishedStatusAsync(publisher, RunStatus.Aborted);
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
            DeleteDatabaseDirectory(databasePath);
        }
    }

    [Fact]
    public void CancelRequest_WithNoRegisteredRun_ReturnsFalse()
    {
        var registry = new InMemoryRunCancellationRegistry();
        Assert.False(registry.TryCancel("unknown-run"));
    }

    [Fact]
    public async Task RunTimesOut_WhenExceedingConfiguredDuration()
    {
        var databasePath = CreateDatabasePath();
        var queue = new ChannelRunQueue();
        var store = new SqliteRunStore(databasePath);
        var registry = new InMemoryRunCancellationRegistry();
        var executor = new BlockingExecutor();
        var publisher = new RecordingProgressPublisher();
        var service = CreateService(queue, store, executor, publisher, registry, runTimeout: TimeSpan.FromMilliseconds(100));
        var run = CreateRun("run-timeout");

        try
        {
            await store.CreateAsync(run);
            await service.StartAsync(CancellationToken.None);
            await queue.EnqueueAsync(run.RunId);
            await executor.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));

            var persisted = await WaitForTerminalRunAsync(store, run.RunId);
            Assert.NotNull(persisted);
            Assert.Equal(RunStatus.Aborted, persisted.Status);
            Assert.Equal("Run timed out after 0 minutes.", persisted.Error);
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
            DeleteDatabaseDirectory(databasePath);
        }
    }

    private static RunBackgroundService CreateService(
        IRunQueue queue,
        IRunStore store,
        IRunExecutor executor,
        IRunProgressPublisher progressPublisher,
        IRunCancellationRegistry? cancellationRegistry = null,
        TimeSpan? runTimeout = null)
    {
        return new RunBackgroundService(
            queue,
            store,
            executor,
            progressPublisher,
            cancellationRegistry ?? new InMemoryRunCancellationRegistry(),
            NullLogger<RunBackgroundService>.Instance,
            runTimeout);
    }

    private static RunRecord CreateRun(string runId)
    {
        return new RunRecord
        {
            RunId = runId,
            AgentName = "test-agent",
            UserMessage = "test",
        };
    }

    private static async Task<RunRecord?> WaitForTerminalRunAsync(IRunStore store, string runId)
    {
        for (var attempt = 0; attempt < 50; attempt++)
        {
            var run = await store.GetAsync(runId);
            if (run?.Status is RunStatus.Succeeded or RunStatus.Failed or RunStatus.Aborted)
            {
                return run;
            }

            await Task.Delay(10);
        }

        return await store.GetAsync(runId);
    }

    private static async Task WaitForPublishedStatusAsync(RecordingProgressPublisher publisher, RunStatus status)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            if (publisher.Updates.Any(run => run.Status == status))
            {
                return;
            }

            await Task.Delay(10);
        }

        Assert.Contains(publisher.Updates, run => run.Status == status);
    }

    private static string CreateDatabasePath()
    {
        return Path.Combine(Path.GetTempPath(), "work-agents-tests", $"{Guid.NewGuid():N}", "runs.db");
    }

    private static void DeleteDatabaseDirectory(string databasePath)
    {
        var directory = Path.GetDirectoryName(databasePath);
        if (directory is not null && Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private sealed class RecordingExecutor : IRunExecutor
    {
        public TaskCompletionSource<RunRecord> FirstExecution { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Exception? Exception { get; init; }

        public int Calls { get; private set; }

        public Task<string> ExecuteAsync(RunRecord run, CancellationToken ct = default)
        {
            Calls++;
            FirstExecution.TrySetResult(run);
            if (Exception is not null)
            {
                throw Exception;
            }

            return Task.FromResult("completed");
        }
    }

    private sealed class BlockingExecutor : IRunExecutor
    {
        public TaskCompletionSource<RunRecord> Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<string> ExecuteAsync(RunRecord run, CancellationToken ct = default)
        {
            Started.TrySetResult(run);
            await Task.Delay(Timeout.Infinite, ct);
            return "unreachable";
        }
    }

    private sealed class RecordingProgressPublisher : IRunProgressPublisher
    {
        public ConcurrentQueue<RunRecord> Updates { get; } = new();

        public Task PublishAsync(RunRecord run, CancellationToken ct = default)
        {
            Updates.Enqueue(run);
            return Task.CompletedTask;
        }
    }
}