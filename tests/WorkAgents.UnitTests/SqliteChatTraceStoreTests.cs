using WorkAgents.Core;
using WorkAgents.Infrastructure.Stores;

namespace WorkAgents.UnitTests;

public sealed class SqliteChatTraceStoreTests
{
    [Fact]
    public async Task ListAsync_ReturnsEntriesForThreadInInsertOrder()
    {
        var databasePath = CreateDatabasePath();
        try
        {
            var store = new SqliteChatTraceStore(databasePath);
            await store.AppendAsync(CreateEntry("thread-1", durationMs: 120));
            await store.AppendAsync(CreateEntry("thread-1", durationMs: 340));
            await store.AppendAsync(CreateEntry("thread-2", durationMs: 999));

            var entries = await store.ListAsync("thread-1");

            Assert.Equal(2, entries.Count);
            Assert.Equal(120, entries[0].DurationMs);
            Assert.Equal(340, entries[1].DurationMs);
        }
        finally
        {
            DeleteDatabaseDirectory(databasePath);
        }
    }

    [Fact]
    public async Task ListAsync_ReturnsEmpty_ForUnknownThread()
    {
        var databasePath = CreateDatabasePath();
        try
        {
            var store = new SqliteChatTraceStore(databasePath);
            Assert.Empty(await store.ListAsync("unknown-thread"));
        }
        finally
        {
            DeleteDatabaseDirectory(databasePath);
        }
    }

    [Fact]
    public async Task AppendAsync_PersistsFailureDetails()
    {
        var databasePath = CreateDatabasePath();
        try
        {
            var store = new SqliteChatTraceStore(databasePath);
            await store.AppendAsync(new ChatTraceEntry
            {
                ThreadId = "thread-1",
                AgentName = "meeting-agent",
                ModelName = "gpt-5.6-luna",
                Provider = "Foundry",
                DurationMs = 42,
                Success = false,
                ErrorMessage = "boom",
            });

            var entry = Assert.Single(await store.ListAsync("thread-1"));

            Assert.False(entry.Success);
            Assert.Equal("boom", entry.ErrorMessage);
            Assert.Equal("gpt-5.6-luna", entry.ModelName);
            Assert.Equal("Foundry", entry.Provider);
        }
        finally
        {
            DeleteDatabaseDirectory(databasePath);
        }
    }

    private static ChatTraceEntry CreateEntry(string threadId, long durationMs) => new()
    {
        ThreadId = threadId,
        AgentName = "meeting-agent",
        ModelName = "gpt-5.6-luna",
        Provider = "Foundry",
        DurationMs = durationMs,
    };

    private static string CreateDatabasePath()
        => Path.Combine(Path.GetTempPath(), "work-agents-tests", $"{Guid.NewGuid():N}", "chat-traces.db");

    private static void DeleteDatabaseDirectory(string databasePath)
    {
        var directory = Path.GetDirectoryName(databasePath);
        if (directory is not null && Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
