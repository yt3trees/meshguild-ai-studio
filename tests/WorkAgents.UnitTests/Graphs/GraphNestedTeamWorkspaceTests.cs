using WorkAgents.Core.Graphs;
using WorkAgents.Core.Abstractions;
using WorkAgents.Core.Missions;
using WorkAgents.Core.Teams;
using WorkAgents.Infrastructure.Execution;
using WorkAgents.Infrastructure.Stores;
using WorkAgents.Orchestration;
using WorkAgents.Orchestration.Admission;
using WorkAgents.Orchestration.Graph;
using WorkAgents.Orchestration.Teams;
using WorkAgents.UnitTests.Fakes;
using WorkAgents.UnitTests.Support;

namespace WorkAgents.UnitTests.Graphs;

public sealed class GraphNestedTeamWorkspaceTests
{
    [Fact]
    public async Task Execute_TeamNodeUsesParentMissionWorkspaceThroughHandler()
    {
        using var paths = new MissionWorkspaceTestPaths();
        var workingDirectory = paths.MissionWorkspace("mission");
        var graph = new GraphDefinition
        {
            Name = "graph",
            Nodes = [new GraphNode { Id = "team", Kind = NodeKind.Team, Team = "team", Input = "input" }],
            Edges = [],
        };
        string? observedDirectory = null;

        var result = await new GraphExecutor(new ScriptedAgentInvoker()).ExecuteAsync(new GraphExecutionRequest
        {
            MissionId = "mission",
            Goal = "goal",
            Graph = graph,
            WorkingDirectory = workingDirectory,
            TeamHandler = (node, input, _) =>
            {
                observedDirectory = workingDirectory;
                Directory.CreateDirectory(workingDirectory);
                File.WriteAllText(Path.Combine(workingDirectory, "team.txt"), input);
                return Task.FromResult("team complete");
            },
        });

        Assert.Equal(NodeRunState.Succeeded, result.NodeRuns.Single().State);
        Assert.Equal(workingDirectory, observedDirectory);
        Assert.Equal("input", await File.ReadAllTextAsync(Path.Combine(workingDirectory, "team.txt")));
    }

    [Fact]
    public async Task MissionEngine_ExecutesNestedTeamInParentWorkspace()
    {
        using var paths = new MissionWorkspaceTestPaths();
        var messageStore = new SqliteMessageStore(paths.DatabasePath);
        var missions = new SqliteMissionStore(paths.DatabasePath);
        var workspaceStore = new SqliteMissionWorkspaceStore(paths.DatabasePath);
        var workspaceProvider = new MissionWorkspaceProvider(new MissionWorkspacePathResolver(paths.Root), workspaceStore);
        var invoker = new NestedTeamInvoker();
        var team = new TeamDefinition
        {
            Name = "inner-team",
            Orchestrator = new TeamOrchestrator { Agent = "orchestrator" },
            Members = [new TeamMember { Agent = "writer" }],
        };
        var graph = new GraphDefinition
        {
            Name = "graph",
            Nodes = [new GraphNode { Id = "team", Kind = NodeKind.Team, Team = team.Name, Input = "start" }],
            Edges = [],
        };
        var engine = new MissionEngine(
            missions,
            new AdmissionController(new SqliteMissionQueueStore(paths.DatabasePath), 5, 12),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<MissionEngine>.Instance,
            teamExecutor: new TeamExecutor(invoker, new MessageBus(messageStore)),
            teams: [team],
            graphExecutor: new GraphExecutor(new ScriptedAgentInvoker()),
            graphs: [graph],
            workspaceProvider: workspaceProvider);
        await missions.CreateAsync(new Mission
        {
            MissionId = "mission",
            Goal = "run inner team",
            TargetKind = MissionTargetKind.Graph,
            TargetName = graph.Name,
        });

        await engine.StartAsync("mission");
        var execution = engine.GetExecutionTask("mission");
        if (execution is not null)
        {
            await execution;
        }

        var completed = await missions.GetAsync("mission");
        Assert.Equal(MissionStatus.Succeeded, completed!.Status);
        Assert.Equal(2, invoker.WorkingDirectories.Count);
        var workspace = Assert.Single(invoker.WorkingDirectories.Distinct(StringComparer.OrdinalIgnoreCase));
        Assert.Equal(Path.Combine(paths.Root, "missions", "mission", "work"), workspace);
        Assert.Equal("write", await File.ReadAllTextAsync(Path.Combine(workspace, "nested.txt")));
    }

    private sealed class NestedTeamInvoker : IAgentInvoker
    {
        public List<string> WorkingDirectories { get; } = [];

        public Task<AgentInvocationResult> InvokeAsync(AgentInvocation invocation, CancellationToken ct = default)
        {
            WorkingDirectories.Add(invocation.WorkingDirectory ?? string.Empty);
            if (invocation.AgentName == "orchestrator")
            {
                return Task.FromResult(new AgentInvocationResult
                {
                    Utterance = "delegate",
                    ToolCalls =
                    [new AgentToolCall { ToolName = "delegate_task", ArgsSummary = "{\"agent\":\"writer\",\"instruction\":\"write\"}" }],
                });
            }

            Directory.CreateDirectory(invocation.WorkingDirectory!);
            File.WriteAllText(Path.Combine(invocation.WorkingDirectory!, "nested.txt"), invocation.Context);
            return Task.FromResult(new AgentInvocationResult { Utterance = "nested team complete" });
        }
    }
}
