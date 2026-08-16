using WorkAgents.Core;
using WorkAgents.Orchestration.Migration;

namespace WorkAgents.UnitTests.Migration;

public sealed class WorkflowToGraphConverterTests
{
    [Fact]
    public void Convert_PreservesLinearTopologicalOrderAndRewritesInputReferences()
    {
        var workflow = new WorkflowDefinition
        {
            Name = "legacy",
            FolderPath = Path.Combine("workflows", "legacy"),
            Steps =
            [
                new WorkflowStep { Name = "research", Kind = WorkflowStepKind.Agent, Agent = "research", Input = "${workflow.input}" },
                new WorkflowStep { Name = "build", Kind = WorkflowStepKind.Code, Code = "return 1;", Input = "${steps.research.output}" },
                new WorkflowStep { Name = "approve", Kind = WorkflowStepKind.Approve, Title = "Review" },
            ],
            ScheduleCron = "0 * * * *",
        };

        var result = new WorkflowToGraphConverter().Convert(workflow);

        Assert.Equal(new[] { "research", "build", "approve" }, result.TopologicalOrder);
        Assert.Equal(new[] { "research", "build", "approve" }, result.Graph.Nodes.Select(node => node.Id));
        Assert.Equal(new[] { "to-build", "to-approve" }, result.Graph.Edges.Select(edge => edge.Id));
        Assert.Equal("${mission.goal}", result.Graph.Nodes[0].Input);
        Assert.Equal("${nodes.research.output}", result.Graph.Nodes[1].Input);
        Assert.Equal("0 * * * *", result.ScheduleCron);
    }
}
