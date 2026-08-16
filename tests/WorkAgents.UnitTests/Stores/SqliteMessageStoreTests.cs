using WorkAgents.Core.Missions;
using WorkAgents.Infrastructure.Stores;

namespace WorkAgents.UnitTests.Stores;

public sealed class SqliteMessageStoreTests
{
    [Fact]
    public async Task AppendAsync_AssignsMonotonicSeqPerMission()
    {
        var databasePath = CreateDatabasePath();
        try
        {
            var store = new SqliteMessageStore(databasePath);
            var m1 = await store.AppendAsync(CreateMessage("mission-1"));
            var m2 = await store.AppendAsync(CreateMessage("mission-1"));
            var other = await store.AppendAsync(CreateMessage("mission-2"));

            Assert.Equal(1, m1.Seq);
            Assert.Equal(2, m2.Seq);
            Assert.Equal(1, other.Seq);
        }
        finally
        {
            DeleteDatabaseDirectory(databasePath);
        }
    }

    [Fact]
    public async Task ListAsync_ReturnsInSeqOrderSinceGivenSeq()
    {
        var databasePath = CreateDatabasePath();
        try
        {
            var store = new SqliteMessageStore(databasePath);
            await store.AppendAsync(CreateMessage("mission-1"));
            await store.AppendAsync(CreateMessage("mission-1"));
            await store.AppendAsync(CreateMessage("mission-1"));

            var messages = await store.ListAsync("mission-1", sinceSeq: 1);

            Assert.Equal(2, messages.Count);
            Assert.Equal(2, messages[0].Seq);
            Assert.Equal(3, messages[1].Seq);
        }
        finally
        {
            DeleteDatabaseDirectory(databasePath);
        }
    }

    [Fact]
    public async Task DiscardAsync_ExcludesDiscardedMessagesByDefault()
    {
        var databasePath = CreateDatabasePath();
        try
        {
            var store = new SqliteMessageStore(databasePath);
            await store.AppendAsync(CreateMessage("mission-1"));
            await store.AppendAsync(CreateMessage("mission-1"));
            await store.AppendAsync(CreateMessage("mission-1"));

            await store.DiscardAsync("mission-1", afterSeq: 1, checkpointId: "cp-1");

            var visible = await store.ListAsync("mission-1");
            Assert.Single(visible);

            var withDiscarded = await store.ListAsync("mission-1", includeDiscarded: true);
            Assert.Equal(3, withDiscarded.Count);
            Assert.All(withDiscarded.Skip(1), m => Assert.NotNull(m.DiscardedAt));
        }
        finally
        {
            DeleteDatabaseDirectory(databasePath);
        }
    }

    private static Message CreateMessage(string missionId) => new()
    {
        MessageId = Guid.NewGuid().ToString("N"),
        MissionId = missionId,
        Seq = 0,
        SenderKind = MessageSenderKind.System,
        Kind = MessageKind.SystemNote,
        Body = "note",
    };

    private static string CreateDatabasePath()
        => Path.Combine(Path.GetTempPath(), "work-agents-tests", $"{Guid.NewGuid():N}", "messages.db");

    private static void DeleteDatabaseDirectory(string databasePath)
    {
        var directory = Path.GetDirectoryName(databasePath);
        if (directory is not null && Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
