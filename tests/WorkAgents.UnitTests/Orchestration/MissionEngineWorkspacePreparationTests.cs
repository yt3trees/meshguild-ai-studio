using WorkAgents.Core.Abstractions;
using WorkAgents.Core.Graphs;
using WorkAgents.Core.Missions;
using WorkAgents.Infrastructure.Stores;
using WorkAgents.Orchestration;
using WorkAgents.Orchestration.Admission;
using WorkAgents.Orchestration.Graph;
using WorkAgents.UnitTests.Support;

namespace WorkAgents.UnitTests.Orchestration;

public sealed class MissionEngineWorkspacePreparationTests
{
    [Fact]
    public async Task StartAsync_PreparesWorkspaceBeforeFirstAgentInvocation()
    {
        using var paths = new MissionWorkspaceTestPaths();
        var events = new List<string>();
        var database = new SqliteMissionStore(paths.DatabasePath);
        var graph = CreateGraph();
        var invoker = new RecordingInvoker(() => events.Add("invoked"));
        var provider = new RecordingWorkspaceProvider(
            Path.Combine(paths.Root, "missions", "mission", "work"),
            () => events.Add("prepared"));
        var engine = CreateEngine(database, paths.DatabasePath, graph, invoker, provider);
        await database.CreateAsync(CreateMission());

        await engine.StartAsync("mission");
        var execution = engine.GetExecutionTask("mission");
        if (execution is not null)
        {
            await execution;
        }

        var completed = await database.GetAsync("mission");
        Assert.Equal(MissionStatus.Succeeded, completed!.Status);
        Assert.Equal(["prepared", "invoked"], events);
    }

    [Fact]
    public async Task StartAsync_PreparationFailureFailsMissionBeforeInvocation()
    {
        using var paths = new MissionWorkspaceTestPaths();
        var database = new SqliteMissionStore(paths.DatabasePath);
        var graph = CreateGraph();
        var invoker = new RecordingInvoker();
        var provider = new RecordingWorkspaceProvider(
            Path.Combine(paths.Root, "missions", "mission", "work"),
            prepare: null,
            exception: new InvalidOperationException("secret physical path"));
        var engine = CreateEngine(database, paths.DatabasePath, graph, invoker, provider);
        await database.CreateAsync(CreateMission());

        await engine.StartAsync("mission");

        var failed = await database.GetAsync("mission");
        Assert.Equal(MissionStatus.Failed, failed!.Status);
        Assert.Equal(MissionOutcome.Failed, failed.Outcome);
        Assert.Equal("Mission workspace could not be prepared.", failed.Error);
        Assert.Empty(invoker.Invocations);
    }

    private static MissionEngine CreateEngine(
        IMissionStore missions,
        string databasePath,
        GraphDefinition graph,
        IAgentInvoker invoker,
        IMissionWorkspaceProvider provider)
    {
        var admission = new AdmissionController(new SqliteMissionQueueStore(databasePath), 5, 12);
        return new MissionEngine(
            missions,
            admission,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<MissionEngine>.Instance,
            graphExecutor: new GraphExecutor(invoker),
            graphs: [graph],
            workspaceProvider: provider);
    }

    private static GraphDefinition CreateGraph()
        => new()
        {
            Name = "graph",
            Nodes = [new GraphNode { Id = "run", Kind = NodeKind.Agent, Agent = "agent", Input = "goal" }],
            Edges = [],
        };

    private static Mission CreateMission()
        => new()
        {
            MissionId = "mission",
            Goal = "goal",
            TargetKind = MissionTargetKind.Graph,
            TargetName = "graph",
        };

    private sealed class RecordingWorkspaceProvider : IMissionWorkspaceProvider
    {
        private readonly Action? _prepare;
        private readonly Exception? _exception;

        public RecordingWorkspaceProvider(string path, Action? prepare, Exception? exception = null)
        {
            Path = path;
            _prepare = prepare;
            _exception = exception;
        }

        public string Path { get; }

        public string ResolvePath(string missionId) => Path;

        public Task<string> PrepareAsync(string missionId, CancellationToken ct = default)
        {
            if (_exception is not null)
            {
                throw _exception;
            }
            _prepare?.Invoke();
            return Task.FromResult(Path);
        }
    }

    private sealed class RecordingInvoker : IAgentInvoker
    {
        private readonly Action? _onInvoke;

        public RecordingInvoker(Action? onInvoke = null)
        {
            _onInvoke = onInvoke;
        }

        public List<AgentInvocation> Invocations { get; } = [];

        public Task<AgentInvocationResult> InvokeAsync(AgentInvocation invocation, CancellationToken ct = default)
        {
            Invocations.Add(invocation);
            _onInvoke?.Invoke();
            return Task.FromResult(new AgentInvocationResult { Utterance = "done" });
        }
    }
}
