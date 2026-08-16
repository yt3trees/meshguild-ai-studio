using WorkAgents.Core.Graphs;
using WorkAgents.Orchestration.Graph;
using WorkAgents.UnitTests.Fakes;
using WorkAgents.UnitTests.Support;

namespace WorkAgents.UnitTests.Graphs;

public sealed class GraphExecutorTests
{
    [Fact]
    public async Task Execute_BranchRunsOnlyTheSelectedSide()
    {
        var graph = new GraphDefinition
        {
            Name = "graph",
            Nodes =
            [
                new GraphNode { Id = "start", Kind = NodeKind.Code, Input = "go", CodeFile = "start.csx" },
                new GraphNode { Id = "route", Kind = NodeKind.Branch },
                new GraphNode { Id = "selected", Kind = NodeKind.Code, CodeFile = "selected.csx" },
                new GraphNode { Id = "fallback", Kind = NodeKind.Code, CodeFile = "fallback.csx" },
            ],
            Edges =
            [
                new GraphEdge { Id = "e1", From = "start", To = "route" },
                new GraphEdge { Id = "selected", From = "route", To = "selected", Condition = "${nodes.start.output} == 'go'" },
                new GraphEdge { Id = "fallback", From = "route", To = "fallback" },
            ],
        };
        var executor = new GraphExecutor(new ScriptedAgentInvoker());

        var result = await executor.ExecuteAsync(new GraphExecutionRequest
        {
            MissionId = "mission",
            Goal = "goal",
            Graph = graph,
        });

        Assert.Contains("selected", result.Outputs.Keys);
        Assert.DoesNotContain("fallback", result.Outputs.Keys);
        Assert.Contains(result.EdgeTransits, transit => transit.EdgeId == "selected");
    }

    [Fact]
    public async Task Execute_UsesCodeHandlerForDeterministicNodes()
    {
        var graph = new GraphDefinition
        {
            Name = "graph",
            Nodes = [new GraphNode { Id = "one", Kind = NodeKind.Code, Input = "input", CodeFile = "one.csx" }],
            Edges = [],
        };

        var result = await new GraphExecutor(new ScriptedAgentInvoker()).ExecuteAsync(new GraphExecutionRequest
        {
            MissionId = "mission",
            Goal = "goal",
            Graph = graph,
            CodeHandler = (node, input, _) => Task.FromResult(input + "-done"),
        });

        Assert.Equal("input-done", result.Outputs["one"]);
        Assert.Equal(NodeRunState.Succeeded, result.NodeRuns.Single().State);
    }

    [Fact]
    public async Task Execute_AgentNodeReceivesMissionWorkingDirectory()
    {
        using var paths = new MissionWorkspaceTestPaths();
        var workingDirectory = paths.MissionWorkspace("mission");
        var invoker = new ScriptedAgentInvoker().Script("agent", "done");
        var graph = new GraphDefinition
        {
            Name = "graph",
            Nodes = [new GraphNode { Id = "run", Kind = NodeKind.Agent, Agent = "agent", Input = "input" }],
            Edges = [],
        };

        await new GraphExecutor(invoker).ExecuteAsync(new GraphExecutionRequest
        {
            MissionId = "mission",
            Goal = "goal",
            Graph = graph,
            WorkingDirectory = workingDirectory,
        });

        Assert.Equal(workingDirectory, Assert.Single(invoker.Invocations).WorkingDirectory);
    }
}
