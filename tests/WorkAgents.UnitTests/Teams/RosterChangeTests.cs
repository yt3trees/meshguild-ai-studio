using WorkAgents.Core.Missions;
using WorkAgents.Core.Teams;
using WorkAgents.Infrastructure.Stores;
using WorkAgents.Orchestration.Teams;

namespace WorkAgents.UnitTests.Teams;

public sealed class RosterChangeTests
{
    [Fact]
    public async Task AddAndRemoveParticipant_RecordsReasonAndUpdatesState()
    {
        var databasePath = TestPaths.CreateDatabasePath();
        try
        {
            var instances = new SqliteAgentInstanceStore(databasePath);
            var messages = new MessageBus(new SqliteMessageStore(databasePath));
            var manager = new RosterManager(instances, messages);
            var team = new TeamDefinition
            {
                Name = "team",
                Orchestrator = new TeamOrchestrator { Agent = "orchestrator" },
                Members = [new TeamMember { Agent = "dev", MaxInstances = 2 }],
                Limits = new TeamLimits { MaxParallelInstances = 3 },
            };

            var added = await manager.AddParticipantAsync("mission", team, "dev", "missing expertise");
            var instance = await instances.GetAsync(added.InstanceId!);
            var removed = await manager.RemoveParticipantAsync("mission", team, instance!.InstanceId, "work complete");

            Assert.True(added.Accepted);
            Assert.True(removed.Accepted);
            var final = await instances.GetAsync(instance.InstanceId);
            Assert.Equal(AgentInstanceState.Stopped, final!.State);
            Assert.Equal("work complete", final.LeaveReason);
        }
        finally
        {
            TestPaths.DeleteDatabaseDirectory(databasePath);
        }
    }

    [Fact]
    public async Task AddParticipant_RejectsAgentOutsideDefinition()
    {
        var databasePath = TestPaths.CreateDatabasePath();
        try
        {
            var manager = new RosterManager(
                new SqliteAgentInstanceStore(databasePath),
                new MessageBus(new SqliteMessageStore(databasePath)));
            var result = await manager.AddParticipantAsync("mission", new TeamDefinition
            {
                Name = "team",
                Orchestrator = new TeamOrchestrator { Agent = "orchestrator" },
                Members = [new TeamMember { Agent = "dev" }],
            }, "unknown", "test");

            Assert.False(result.Accepted);
            Assert.Equal("not_in_team", result.Code);
        }
        finally
        {
            TestPaths.DeleteDatabaseDirectory(databasePath);
        }
    }
}
