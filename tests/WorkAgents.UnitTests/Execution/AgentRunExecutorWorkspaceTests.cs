using WorkAgents.Agents;
using WorkAgents.Agents.Loading;
using WorkAgents.Core;
using WorkAgents.Core.Abstractions;

namespace WorkAgents.UnitTests.Execution;

public sealed class AgentRunExecutorWorkspaceTests
{
    [Fact]
    public async Task ExecuteAsync_KeepsStandaloneRunDirectoriesIsolated()
    {
        var root = Path.Combine(Path.GetTempPath(), "work-agents-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var registry = new RecordingAgentRegistry();
            var executor = new AgentRunExecutor(registry, new ProfileOptions { WorkspaceRoot = root });

            await executor.ExecuteAsync(new RunRecord
            {
                RunId = "run-one",
                AgentName = "agent",
                UserMessage = "one",
            });
            await executor.ExecuteAsync(new RunRecord
            {
                RunId = "run-two",
                AgentName = "agent",
                UserMessage = "two",
            });

            Assert.Equal(
                [Path.Combine(root, "run-one"), Path.Combine(root, "run-two")],
                registry.WorkingDirectories);
            Assert.NotEqual(registry.WorkingDirectories[0], registry.WorkingDirectories[1]);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ExecuteAsync_RejectsRunWorkspaceOutsideRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "work-agents-tests", Guid.NewGuid().ToString("N"));
        var executor = new AgentRunExecutor(new RecordingAgentRegistry(), new ProfileOptions { WorkspaceRoot = root });

        await Assert.ThrowsAsync<InvalidOperationException>(() => executor.ExecuteAsync(new RunRecord
        {
            RunId = "../escape",
            AgentName = "agent",
            UserMessage = "escape",
        }));
    }

    private sealed class RecordingAgentRegistry : IAgentRegistry
    {
        public List<string> WorkingDirectories { get; } = [];

        public IReadOnlyList<AgentView> ListAgents() => [];

        public IReadOnlyList<ToolView> ListTools() => [];

        public Task<string> RunAsync(string agentName, string userMessage, CancellationToken cancellationToken = default)
            => Task.FromResult("ok");

        public Task<string> RunAsync(string agentName, string userMessage, string workingDirectory, CancellationToken cancellationToken = default)
            => RecordAsync(workingDirectory);

        public Task<string> RunAsync(string agentName, string userMessage, string workingDirectory, string runId, CancellationToken cancellationToken = default)
            => RecordAsync(workingDirectory);

        public Task<string> RunAsync(string agentName, string userMessage, string? workingDirectory, string? threadId, string? runId, CancellationToken cancellationToken = default)
            => RecordAsync(workingDirectory);

        public async IAsyncEnumerable<AgentInvocationUpdate> RunStreamingAsync(
            string agentName,
            string userMessage,
            string? workingDirectory,
            string? threadId,
            string? runId,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var utterance = await RecordAsync(workingDirectory);
            yield return new AgentTextDeltaUpdate(utterance);
            yield return new AgentCompletedUpdate(new AgentInvocationResult { Utterance = utterance });
        }

        private Task<string> RecordAsync(string? workingDirectory)
        {
            WorkingDirectories.Add(workingDirectory ?? string.Empty);
            return Task.FromResult("ok");
        }
    }
}
