using WorkAgents.Core;
using WorkAgents.Infrastructure.Stores;

namespace WorkAgents.UnitTests;

public sealed class SqliteCostStoreTests
{
    [Fact]
    public async Task ListAsync_ReturnsRecordsSinceGivenTime()
    {
        var databasePath = CreateDatabasePath();
        try
        {
            var store = new SqliteCostStore(databasePath);
            var old = DateTimeOffset.UtcNow.AddDays(-2);
            var recent = DateTimeOffset.UtcNow;
            await store.RecordAsync(CreateRecord("repo-agent", createdAt: old));
            await store.RecordAsync(CreateRecord("meeting-agent", createdAt: recent));

            var records = await store.ListAsync(DateTimeOffset.UtcNow.AddDays(-1));

            var record = Assert.Single(records);
            Assert.Equal("meeting-agent", record.AgentName);
        }
        finally
        {
            DeleteDatabaseDirectory(databasePath);
        }
    }

    [Fact]
    public async Task RecordAsync_PersistsRunIdThreadIdAndTokenCounts()
    {
        var databasePath = CreateDatabasePath();
        try
        {
            var store = new SqliteCostStore(databasePath);
            await store.RecordAsync(new CostRecord
            {
                RunId = "run-1",
                ThreadId = "thread-1",
                AgentName = "repo-agent",
                ModelName = "gpt-5.6-luna",
                Provider = "Foundry",
                InputTokens = 120,
                OutputTokens = 45,
                TotalTokens = 165,
            });

            var record = Assert.Single(await store.ListAsync(DateTimeOffset.UtcNow.AddMinutes(-1)));

            Assert.Equal("run-1", record.RunId);
            Assert.Equal("thread-1", record.ThreadId);
            Assert.Equal(120, record.InputTokens);
            Assert.Equal(45, record.OutputTokens);
            Assert.Equal(165, record.TotalTokens);
        }
        finally
        {
            DeleteDatabaseDirectory(databasePath);
        }
    }

    [Fact]
    public async Task RecordAsync_AllowsNullRunIdForSynchronousChatCalls()
    {
        var databasePath = CreateDatabasePath();
        try
        {
            var store = new SqliteCostStore(databasePath);
            await store.RecordAsync(new CostRecord
            {
                RunId = null,
                ThreadId = "thread-1",
                AgentName = "meeting-agent",
            });

            var record = Assert.Single(await store.ListAsync(DateTimeOffset.UtcNow.AddMinutes(-1)));

            Assert.Null(record.RunId);
            Assert.Null(record.InputTokens);
        }
        finally
        {
            DeleteDatabaseDirectory(databasePath);
        }
    }

    private static CostRecord CreateRecord(string agentName, DateTimeOffset createdAt) => new()
    {
        AgentName = agentName,
        CreatedAt = createdAt,
    };

    private static string CreateDatabasePath()
        => Path.Combine(Path.GetTempPath(), "work-agents-tests", $"{Guid.NewGuid():N}", "costs.db");

    private static void DeleteDatabaseDirectory(string databasePath)
    {
        var directory = Path.GetDirectoryName(databasePath);
        if (directory is not null && Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
