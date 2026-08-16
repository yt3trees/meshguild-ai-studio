using WorkAgents.Core.Abstractions;
using WorkAgents.Core.Missions;
using WorkAgents.Core.Teams;
using WorkAgents.Infrastructure.Stores;
using WorkAgents.Orchestration.Teams;
using WorkAgents.UnitTests.Fakes;
using WorkAgents.UnitTests.Support;

namespace WorkAgents.UnitTests.Teams;

public sealed class TeamExecutorStreamingTests
{
    [Fact]
    public async Task Execute_StreamsDeltasAndStillCommitsTheSettledMessage()
    {
        using var paths = new MissionWorkspaceTestPaths();
        var databasePath = TestPaths.CreateDatabasePath();
        try
        {
            var messageStore = new SqliteMessageStore(databasePath);
            var invoker = new ScriptedAgentInvoker { ChunkSize = 4 }
                .Script("orchestrator", "the orchestrator reports progress.");
            var sink = new RecordingAgentStreamSink();
            var executor = new TeamExecutor(
                invoker,
                new MessageBus(messageStore),
                streams: sink);

            await executor.ExecuteAsync(BuildRequest(paths));

            var stream = Assert.Single(sink.Started);
            Assert.Equal("mission", stream.MissionId);
            Assert.Equal("orchestrator", stream.AgentName);

            // 増分を順に連結すると確定した発言と一致する。
            Assert.Equal("the orchestrator reports progress.", sink.TextOf(stream.StreamId));
            Assert.Equal(
                Enumerable.Range(0, sink.Deltas.Count).Select(index => (long)index),
                sink.Deltas.Select(delta => delta.SeqInStream));

            var completed = Assert.Single(sink.Completed);
            Assert.Equal(stream.StreamId, completed.StreamId);
            Assert.False(completed.Interrupted);

            var messages = await messageStore.ListAsync("mission");
            Assert.Contains(messages, message => message.Body == "the orchestrator reports progress.");
        }
        finally
        {
            TestPaths.DeleteDatabaseDirectory(databasePath);
        }
    }

    [Fact]
    public async Task Execute_WithoutSink_UsesTheBatchPath()
    {
        using var paths = new MissionWorkspaceTestPaths();
        var databasePath = TestPaths.CreateDatabasePath();
        try
        {
            var messageStore = new SqliteMessageStore(databasePath);
            var invoker = new ScriptedAgentInvoker().Script("orchestrator", "batched utterance.");
            var executor = new TeamExecutor(invoker, new MessageBus(messageStore));

            await executor.ExecuteAsync(BuildRequest(paths));

            var messages = await messageStore.ListAsync("mission");
            Assert.Contains(messages, message => message.Body == "batched utterance.");
        }
        finally
        {
            TestPaths.DeleteDatabaseDirectory(databasePath);
        }
    }

    [Fact]
    public async Task Execute_WithNullSink_IsTreatedAsDisabled()
    {
        using var paths = new MissionWorkspaceTestPaths();
        var databasePath = TestPaths.CreateDatabasePath();
        try
        {
            var messageStore = new SqliteMessageStore(databasePath);
            var invoker = new ScriptedAgentInvoker().Script("orchestrator", "batched utterance.");
            var executor = new TeamExecutor(
                invoker,
                new MessageBus(messageStore),
                streams: NullAgentStreamSink.Instance);

            await executor.ExecuteAsync(BuildRequest(paths));

            var messages = await messageStore.ListAsync("mission");
            Assert.Contains(messages, message => message.Body == "batched utterance.");
        }
        finally
        {
            TestPaths.DeleteDatabaseDirectory(databasePath);
        }
    }

    private static TeamExecutionRequest BuildRequest(MissionWorkspaceTestPaths paths)
        => new()
        {
            MissionId = "mission",
            Goal = "report progress",
            Team = new TeamDefinition
            {
                Name = "team",
                Orchestrator = new TeamOrchestrator { Agent = "orchestrator" },
                Members = [],
            },
            WorkingDirectory = paths.MissionWorkspace("mission"),
        };
}
