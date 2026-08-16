using System.Diagnostics;
using WorkAgents.Infrastructure.Stores;
using WorkAgents.Orchestration.Teams;

namespace WorkAgents.UnitTests.Performance;

public sealed class MessageLatencyTests
{
    [Fact]
    public async Task MessagePublicationAtTwelveAgentScaleStaysWithinThreeSeconds()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), "work-agents-tests", Guid.NewGuid().ToString("N"), "latency.db");
        try
        {
            var bus = new MessageBus(new SqliteMessageStore(databasePath));
            var stopwatch = Stopwatch.StartNew();
            for (var mission = 0; mission < 5; mission++)
            {
                for (var agent = 0; agent < 12; agent++)
                {
                    await bus.SendAsync($"mission-{mission}", WorkAgents.Core.Missions.MessageSenderKind.Agent, WorkAgents.Core.Missions.MessageKind.Share, $"message-{agent}");
                }
            }
            stopwatch.Stop();

            Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(3), $"message publication took {stopwatch.Elapsed}");
        }
        finally
        {
            var directory = Path.GetDirectoryName(databasePath);
            if (directory is not null && Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }
}
