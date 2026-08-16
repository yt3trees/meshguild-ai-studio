using WorkAgents.Core.Missions;
using WorkAgents.Infrastructure.Stores;

namespace WorkAgents.UnitTests.Mcp;

public sealed class McpSubmissionStoreTests
{
    [Fact]
    public async Task TryCreateAsync_IsIdempotentByRequestKey()
    {
        var root = Path.Combine(Path.GetTempPath(), "work-agents-mcp-tests", Guid.NewGuid().ToString("N"));
        var database = Path.Combine(root, "state.db");
        Directory.CreateDirectory(root);
        try
        {
            var store = new SqliteMcpSubmissionStore(database);
            var first = new McpSubmission
            {
                RequestKey = "request-1",
                RequestHash = "hash-1",
                MissionId = "mission-1",
            };

            Assert.True(await store.TryCreateAsync(first));
            Assert.False(await store.TryCreateAsync(first with { MissionId = "mission-2" }));
            Assert.Equal("mission-1", (await store.GetAsync("request-1"))?.MissionId);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task DeleteExpiredAsync_RemovesOnlyOlderSubmissions()
    {
        var root = Path.Combine(Path.GetTempPath(), "work-agents-mcp-tests", Guid.NewGuid().ToString("N"));
        var database = Path.Combine(root, "state.db");
        Directory.CreateDirectory(root);
        try
        {
            var store = new SqliteMcpSubmissionStore(database);
            await store.TryCreateAsync(new McpSubmission
            {
                RequestKey = "old",
                RequestHash = "hash-old",
                MissionId = "mission-old",
                CreatedAt = DateTimeOffset.UtcNow.AddHours(-2),
            });
            await store.TryCreateAsync(new McpSubmission
            {
                RequestKey = "new",
                RequestHash = "hash-new",
                MissionId = "mission-new",
                CreatedAt = DateTimeOffset.UtcNow,
            });

            await store.DeleteExpiredAsync(DateTimeOffset.UtcNow.AddHours(-1));

            Assert.Null(await store.GetAsync("old"));
            Assert.NotNull(await store.GetAsync("new"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
