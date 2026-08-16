using WorkAgents.Core.Missions;
using WorkAgents.Infrastructure.Stores;
using WorkAgents.Orchestration.Replay;

namespace WorkAgents.UnitTests.Replay;

public sealed class ReplayTests
{
    [Fact]
    public async Task Replay_UsesExecutionOrderAndOmitsDiscardedByDefault()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), "work-agents-tests", Guid.NewGuid().ToString("N"), "replay.db");
        try
        {
            var store = new SqliteMessageStore(databasePath);
            await store.AppendAsync(Create("one"));
            await store.AppendAsync(Create("two"));
            await store.DiscardAsync("mission", 1, "checkpoint");
            var replay = new ReplayService(store);

            var visible = await replay.ReplayAsync("mission");
            var audit = await replay.ReplayAsync("mission", includeDiscarded: true);

            Assert.Single(visible);
            Assert.Equal("one", visible[0].Body);
            Assert.Equal(new[] { "one", "two" }, audit.Select(message => message.Body));
        }
        finally
        {
            var directory = Path.GetDirectoryName(databasePath);
            if (directory is not null && Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    private static Message Create(string body) => new()
    {
        MessageId = Guid.NewGuid().ToString("N"),
        MissionId = "mission",
        Seq = 0,
        SenderKind = MessageSenderKind.Agent,
        Kind = MessageKind.Report,
        Body = body,
    };
}
