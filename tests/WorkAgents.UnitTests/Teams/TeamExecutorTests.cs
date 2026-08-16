using System.Text.Json;
using WorkAgents.Core.Abstractions;
using WorkAgents.Core.Missions;
using WorkAgents.Core.Teams;
using WorkAgents.Infrastructure.Stores;
using WorkAgents.Orchestration.Teams;
using WorkAgents.UnitTests.Fakes;
using WorkAgents.UnitTests.Support;

namespace WorkAgents.UnitTests.Teams;

public sealed class TeamExecutorTests
{
    [Fact]
    public async Task Execute_DelegatesAndRunsOneDirectQuestionAnswerRoundTrip()
    {
        using var paths = new MissionWorkspaceTestPaths();
        var databasePath = TestPaths.CreateDatabasePath();
        try
        {
            var messageStore = new SqliteMessageStore(databasePath);
            var invoker = new ScriptedAgentInvoker()
                .Script("orchestrator", new AgentInvocationResult
                {
                    Utterance = "I will delegate the implementation.",
                    ToolCalls =
                    [
                        new AgentToolCall
                        {
                            ToolName = "delegate_task",
                            ArgsSummary = "{\"agent\":\"dev\",\"instruction\":\"implement the change\"}",
                        },
                    ],
                })
                .Script("dev", new AgentInvocationResult
                {
                    Utterance = "I need the expected behavior.",
                    ToolCalls =
                    [
                        new AgentToolCall
                        {
                            ToolName = "ask_agent",
                            ArgsSummary = "{\"agent\":\"spec\",\"question\":\"what is the expected behavior?\"}",
                        },
                    ],
                })
                .Script("spec", "The expected behavior is deterministic.");
            var executor = new TeamExecutor(invoker, new MessageBus(messageStore));

            var result = await executor.ExecuteAsync(new TeamExecutionRequest
            {
                MissionId = "mission",
                Goal = "complete the feature",
                Team = new TeamDefinition
                {
                    Name = "team",
                    Orchestrator = new TeamOrchestrator { Agent = "orchestrator" },
                    Members = [new TeamMember { Agent = "dev" }, new TeamMember { Agent = "spec" }],
                    ChannelsAllow =
                    [
                        new ChannelRule
                        {
                            From = "dev",
                            To = "spec",
                            Kinds = [WorkAgents.Core.Missions.MessageKind.Question, WorkAgents.Core.Missions.MessageKind.Answer],
                        },
                    ],
                },
                WorkingDirectory = paths.MissionWorkspace("mission"),
            });

            var messages = await messageStore.ListAsync("mission");
            Assert.Equal(MissionOutcome.Succeeded, result.Outcome);
            Assert.Contains(messages, message => message.Kind == WorkAgents.Core.Missions.MessageKind.Delegate);
            Assert.Contains(messages, message => message.Kind == WorkAgents.Core.Missions.MessageKind.Question);
            Assert.Contains(messages, message => message.Kind == WorkAgents.Core.Missions.MessageKind.Answer);
            Assert.Equal(messages.Count, messages.Select(message => message.Seq).Distinct().Count());
            Assert.Equal(3, invoker.Invocations.Count);
            Assert.All(invoker.Invocations, invocation => Assert.Equal(paths.MissionWorkspace("mission"), invocation.WorkingDirectory));
        }
        finally
        {
            TestPaths.DeleteDatabaseDirectory(databasePath);
        }
    }
}

internal static class TestPaths
{
    public static string CreateDatabasePath()
        => Path.Combine(Path.GetTempPath(), "work-agents-tests", Guid.NewGuid().ToString("N"), "state.db");

    public static void DeleteDatabaseDirectory(string databasePath)
    {
        var directory = Path.GetDirectoryName(databasePath);
        if (directory is not null && Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
