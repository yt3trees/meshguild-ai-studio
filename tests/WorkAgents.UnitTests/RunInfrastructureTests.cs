using WorkAgents.Core;
using WorkAgents.Infrastructure.Queue;
using WorkAgents.Infrastructure.Stores;

namespace WorkAgents.UnitTests;

public sealed class RunInfrastructureTests
{
    [Fact]
    public async Task SqliteRunStore_PersistsRunAndRejectsDuplicateTransition()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), "work-agents-tests", $"{Guid.NewGuid():N}", "runs.db");
        try
        {
            var store = new SqliteRunStore(databasePath);
            var run = new RunRecord
            {
                RunId = "run-1",
                AgentName = "repo-agent",
                UserMessage = "inspect the repository",
                ThreadId = "thread-1",
            };

            await store.CreateAsync(run);
            Assert.True(await store.TrySetStatusAsync(run.RunId, RunStatus.Queued, RunStatus.Running));
            Assert.False(await store.TrySetStatusAsync(run.RunId, RunStatus.Queued, RunStatus.Running));

            await store.CompleteAsync(run.RunId, RunStatus.Succeeded, "done");

            var persisted = await store.GetAsync(run.RunId);
            Assert.NotNull(persisted);
            Assert.Equal(RunStatus.Succeeded, persisted.Status);
            Assert.Equal("done", persisted.Result);
            Assert.Equal(run.UserMessage, persisted.UserMessage);
            Assert.NotNull(persisted.StartedAt);
            Assert.NotNull(persisted.CompletedAt);
        }
        finally
        {
            DeleteDatabaseDirectory(databasePath);
        }
    }

    [Fact]
    public async Task ChannelRunQueue_ReadsRunIdsInEnqueueOrder()
    {
        var queue = new ChannelRunQueue(capacity: 2);

        await queue.EnqueueAsync("run-1");
        await queue.EnqueueAsync("run-2");

        using var cancellation = new CancellationTokenSource();
        var values = new List<string>();
        await using var enumerator = queue.ReadAllAsync(cancellation.Token).GetAsyncEnumerator();

        Assert.True(await enumerator.MoveNextAsync());
        values.Add(enumerator.Current);
        Assert.True(await enumerator.MoveNextAsync());
        values.Add(enumerator.Current);
        cancellation.Cancel();

        Assert.Equal(["run-1", "run-2"], values);
    }

    private static void DeleteDatabaseDirectory(string databasePath)
    {
        var directory = Path.GetDirectoryName(databasePath);
        if (directory is not null && Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}