using WorkAgents.Core;
using WorkAgents.Core.Graphs;

namespace WorkAgents.Orchestration.Migration;

public sealed record WorkflowGraphConversionResult(
    GraphDefinition Graph,
    IReadOnlyList<string> TopologicalOrder,
    string? ScheduleCron);

/// <summary>Converts the legacy linear workflow definition into a graph once.</summary>
public sealed class WorkflowToGraphConverter
{
    public WorkflowGraphConversionResult Convert(WorkflowDefinition workflow)
    {
        ArgumentNullException.ThrowIfNull(workflow);
        if (workflow.Steps.Count == 0)
        {
            throw new InvalidOperationException($"Workflow '{workflow.Name}' has no steps.");
        }
        var nodes = workflow.Steps.Select(step => new GraphNode
        {
            Id = step.Name,
            Kind = step.Kind switch
            {
                WorkflowStepKind.Agent => NodeKind.Agent,
                WorkflowStepKind.Code => NodeKind.Code,
                WorkflowStepKind.Approve => NodeKind.Approval,
                _ => throw new InvalidOperationException($"Workflow step '{step.Name}' has unsupported kind '{step.Kind}'."),
            },
            Agent = step.Agent,
            Input = RewriteExpressions(step.Input),
            CodeFile = string.IsNullOrWhiteSpace(step.CodeFile)
                ? null
                : Path.GetRelativePath(workflow.FolderPath, step.CodeFile),
            Title = step.Title,
            Summary = RewriteExpressions(step.Summary),
            TimeoutSeconds = step.Timeout is null ? null : (int)Math.Max(1, step.Timeout.Value.TotalSeconds),
            Next = Array.Empty<string>(),
        }).ToArray();
        var edges = new List<GraphEdge>();
        for (var index = 0; index < workflow.Steps.Count - 1; index++)
        {
            edges.Add(new GraphEdge
            {
                Id = $"to-{workflow.Steps[index + 1].Name}",
                From = workflow.Steps[index].Name,
                To = workflow.Steps[index + 1].Name,
            });
        }
        var byId = nodes.ToDictionary(node => node.Id, StringComparer.Ordinal);
        foreach (var edge in edges)
        {
            byId[edge.From] = byId[edge.From] with { Next = [edge.Id] };
        }
        var graph = new GraphDefinition
        {
            Version = 1,
            Name = workflow.Name,
            DisplayName = workflow.DisplayName,
            Description = workflow.Description,
            Nodes = byId.Values.OrderBy(node => workflow.Steps.Select(step => step.Name).ToList().IndexOf(node.Id)).ToArray(),
            Edges = edges,
            FolderPath = Path.Combine(Path.GetDirectoryName(workflow.FolderPath) ?? string.Empty, workflow.Name),
        };
        return new WorkflowGraphConversionResult(graph, workflow.Steps.Select(step => step.Name).ToArray(), workflow.ScheduleCron);
    }

    public static string RewriteExpressions(string? value)
        => string.IsNullOrEmpty(value)
            ? value ?? string.Empty
            : value.Replace("${workflow.input}", "${mission.goal}", StringComparison.Ordinal)
                .Replace("${steps.", "${nodes.", StringComparison.Ordinal);
}
