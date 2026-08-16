using WorkAgents.Core;
using WorkAgents.Infrastructure.Stores;

namespace WorkAgents.UnitTests;

public sealed class SqliteChatTranscriptStoreTests
{
    [Fact]
    public async Task ListAsync_ReturnsEntriesInInsertOrder()
    {
        var databasePath = CreateDatabasePath();
        try
        {
            var store = new SqliteChatTranscriptStore(databasePath);
            await store.AppendAsync(CreateEntry("thread-1", "meeting-agent", "user", "hello"));
            await store.AppendAsync(CreateEntry("thread-1", "meeting-agent", "agent", "hi there"));
            await store.AppendAsync(CreateEntry("thread-2", "meeting-agent", "user", "other thread"));

            var entries = await store.ListAsync("thread-1");

            Assert.Equal(2, entries.Count);
            Assert.Equal("hello", entries[0].Content);
            Assert.Equal("user", entries[0].Role);
            Assert.Equal("hi there", entries[1].Content);
            Assert.Equal("agent", entries[1].Role);
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
            var store = new SqliteChatTranscriptStore(databasePath);
            Assert.Empty(await store.ListAsync("unknown-thread"));
        }
        finally
        {
            DeleteDatabaseDirectory(databasePath);
        }
    }

    [Fact]
    public async Task GetLatestThreadIdAsync_ReturnsMostRecentlyAppendedThread_ScopedToAgent()
    {
        var databasePath = CreateDatabasePath();
        try
        {
            var store = new SqliteChatTranscriptStore(databasePath);
            await store.AppendAsync(CreateEntry("thread-1", "meeting-agent", "user", "first"));
            await store.AppendAsync(CreateEntry("thread-2", "repo-agent", "user", "different agent"));
            await store.AppendAsync(CreateEntry("thread-3", "meeting-agent", "user", "latest"));

            Assert.Equal("thread-3", await store.GetLatestThreadIdAsync("meeting-agent"));
            Assert.Equal("thread-2", await store.GetLatestThreadIdAsync("repo-agent"));
            Assert.Null(await store.GetLatestThreadIdAsync("unknown-agent"));
        }
        finally
        {
            DeleteDatabaseDirectory(databasePath);
        }
    }

    [Fact]
    public async Task AppendAsync_PersistsIsErrorFlag()
    {
        var databasePath = CreateDatabasePath();
        try
        {
            var store = new SqliteChatTranscriptStore(databasePath);
            await store.AppendAsync(CreateEntry("thread-1", "meeting-agent", "agent", "boom", isError: true));

            var entries = await store.ListAsync("thread-1");

            Assert.True(Assert.Single(entries).IsError);
        }
        finally
        {
            DeleteDatabaseDirectory(databasePath);
        }
    }

    private static ChatTranscriptEntry CreateEntry(
        string threadId, string agentName, string role, string content, bool isError = false) => new()
    {
        ThreadId = threadId,
        AgentName = agentName,
        Role = role,
        Content = content,
        IsError = isError,
    };

    private static string CreateDatabasePath()
        => Path.Combine(Path.GetTempPath(), "work-agents-tests", $"{Guid.NewGuid():N}", "chat-transcripts.db");

    private static void DeleteDatabaseDirectory(string databasePath)
    {
        var directory = Path.GetDirectoryName(databasePath);
        if (directory is not null && Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
