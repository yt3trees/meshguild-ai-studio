using WorkAgents.Core.Missions;
using WorkAgents.Infrastructure.Stores;
using WorkAgents.Orchestration.Context;
using WorkAgents.Orchestration.Teams;

namespace WorkAgents.UnitTests.Orchestration;

public sealed class InterventionTests
{
    [Fact]
    public async Task UnappliedInterventionIsIncludedAndMarkedAfterTheNextTurn()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), "work-agents-tests", Guid.NewGuid().ToString("N"), "intervention.db");
        try
        {
            var messages = new SqliteMessageStore(databasePath);
            var interventions = new SqliteInterventionStore(databasePath);
            var bus = new MessageBus(messages);
            var instruction = await bus.SendAsync("mission", MessageSenderKind.Human, MessageKind.HumanInstruction, "Use the new interpretation.");
            var intervention = new Intervention
            {
                InterventionId = "intervention",
                MissionId = "mission",
                MessageId = instruction.MessageId,
                Body = instruction.Body,
            };
            await interventions.CreateAsync(intervention);
            var context = new ContextAssembler(messages, interventions);

            var assembled = await context.BuildAsync("mission", "instance", "goal");
            var output = await bus.SendAsync("mission", MessageSenderKind.Agent, MessageKind.Report, "acknowledged");
            await context.MarkAppliedAsync(assembled, output.MessageId);

            Assert.Contains("Use the new interpretation.", assembled.Text);
            Assert.Empty(await interventions.ListUnappliedAsync("mission", "instance"));
        }
        finally
        {
            var directory = Path.GetDirectoryName(databasePath);
            if (directory is not null && Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }
}
