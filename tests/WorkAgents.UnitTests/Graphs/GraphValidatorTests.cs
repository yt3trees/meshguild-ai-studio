using WorkAgents.Core.Graphs;
using WorkAgents.Orchestration.Graph;

namespace WorkAgents.UnitTests.Graphs;

public sealed class GraphValidatorTests
{
    [Fact]
    public void Validate_ReportsUndeclaredCycleWithNodesAndEdges()
    {
        var graph = new GraphDefinition
        {
            Name = "graph",
            Nodes =
            [
                new GraphNode { Id = "a", Kind = NodeKind.Code },
                new GraphNode { Id = "b", Kind = NodeKind.Code },
            ],
            Edges =
            [
                new GraphEdge { Id = "a-b", From = "a", To = "b" },
                new GraphEdge { Id = "b-a", From = "b", To = "a" },
            ],
        };

        var error = Assert.Single(new GraphValidator().Validate(graph).Errors, item => item.Code == "undeclared_cycle");

        Assert.Contains("a", error.NodeIds);
        Assert.Contains("b-a", error.EdgeIds);
    }

    [Fact]
    public void Validate_AllowsAnExplicitLoopBackAndRejectsUnknownReferences()
    {
        var graph = new GraphDefinition
        {
            Name = "graph",
            Nodes =
            [
                new GraphNode { Id = "start", Kind = NodeKind.Code, Input = "${nodes.missing.output}" },
                new GraphNode { Id = "end", Kind = NodeKind.Code },
            ],
            Edges =
            [
                new GraphEdge { Id = "forward", From = "start", To = "end" },
                new GraphEdge { Id = "back", From = "end", To = "start", LoopBack = true },
            ],
        };

        var errors = new GraphValidator().Validate(graph).Errors;

        Assert.Contains(errors, error => error.Code == "unresolved_reference");
        Assert.DoesNotContain(errors, error => error.Code == "undeclared_cycle");
    }
}
