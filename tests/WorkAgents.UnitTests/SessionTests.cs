using WorkAgents.Core;
using WorkAgents.Infrastructure.Stores;

namespace WorkAgents.UnitTests;

public sealed class SessionTests
{
    [Fact]
    public async Task Sqlite_session_store_persists_and_updates_same_thread()
    {
        var database = NewDatabasePath();
        try
        {
            var store = new SqliteSessionStore(database);
            var createdAt = DateTimeOffset.Parse("2026-07-20T10:00:00+00:00");
            await store.SaveAsync(new SessionRecord
            {
                ThreadId = "thread-1",
                AgentName = "repo-agent",
                SerializedState = "{\"turns\":1}",
                CreatedAt = createdAt,
                UpdatedAt = createdAt,
            });

            await store.SaveAsync(new SessionRecord
            {
                ThreadId = "thread-1",
                AgentName = "repo-agent",
                SerializedState = "{\"turns\":2}",
                CreatedAt = createdAt,
            });

            var persisted = await store.LoadAsync("thread-1");
            Assert.NotNull(persisted);
            Assert.Equal("repo-agent", persisted.AgentName);
            Assert.Equal("{\"turns\":2}", persisted.SerializedState);
            Assert.Equal(createdAt, persisted.CreatedAt);
            Assert.True(persisted.UpdatedAt >= createdAt);
        }
        finally
        {
            DeleteDatabase(database);
        }
    }

    [Fact]
    public async Task Sqlite_session_store_rejects_cross_agent_thread_reuse()
    {
        var database = NewDatabasePath();
        try
        {
            var store = new SqliteSessionStore(database);
            await store.SaveAsync(new SessionRecord
            {
                ThreadId = "thread-1",
                AgentName = "repo-agent",
                SerializedState = "{}",
            });

            await Assert.ThrowsAsync<InvalidOperationException>(() => store.SaveAsync(new SessionRecord
            {
                ThreadId = "thread-1",
                AgentName = "meeting-agent",
                SerializedState = "{\"overwritten\":true}",
            }));

            var persisted = await store.LoadAsync("thread-1");
            Assert.NotNull(persisted);
            Assert.Equal("repo-agent", persisted.AgentName);
            Assert.Equal("{}", persisted.SerializedState);
        }
        finally
        {
            DeleteDatabase(database);
        }
    }

    [Fact]
    public async Task Sqlite_run_and_session_rows_are_separate()
    {
        var database = NewDatabasePath();
        try
        {
            var runStore = new SqliteRunStore(database);
            var sessionStore = new SqliteSessionStore(database);
            await runStore.CreateAsync(new RunRecord
            {
                RunId = "run-1",
                AgentName = "repo-agent",
                UserMessage = "inspect",
                ThreadId = "thread-1",
            });
            await sessionStore.SaveAsync(new SessionRecord
            {
                ThreadId = "thread-1",
                AgentName = "repo-agent",
                SerializedState = "{}",
            });

            Assert.NotNull(await runStore.GetAsync("run-1"));
            Assert.NotNull(await sessionStore.LoadAsync("thread-1"));
        }
        finally
        {
            DeleteDatabase(database);
        }
    }

    private static string NewDatabasePath()
        => Path.Combine(
            Path.GetTempPath(),
            "work-agents-tests",
            Guid.NewGuid().ToString("N"),
            "state.db");

    private static void DeleteDatabase(string database)
    {
        var directory = Path.GetDirectoryName(database);
        if (!string.IsNullOrEmpty(directory) && Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}