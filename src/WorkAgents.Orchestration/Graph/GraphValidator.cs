using System.Text.RegularExpressions;
using WorkAgents.Core.Graphs;

namespace WorkAgents.Orchestration.Graph;

public sealed record GraphValidationError(
    string Code,
    string Message,
    IReadOnlyList<string> NodeIds,
    IReadOnlyList<string> EdgeIds);

public sealed record GraphValidationResult(IReadOnlyList<GraphValidationError> Errors)
{
    public bool IsValid => Errors.Count == 0;

    public static GraphValidationResult Valid { get; } = new(Array.Empty<GraphValidationError>());
}

/// <summary>Validates graph definitions before they can be persisted or executed.</summary>
public sealed class GraphValidator
{
    // ノード ID にはハイフンを使えるため、参照側も同じ文字集合を許す
    // (許さないと ${nodes.track-a.output} が参照として認識されず、検証をすり抜ける)。
    private static readonly Regex ReferenceRegex = new(@"\$\{(?<reference>[a-zA-Z0-9_.\-]+)\}", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex ConditionTokenRegex = new(
        "^\\s*(?:\\$\\{[a-zA-Z0-9_.\\-]+\\}|true|false|-?\\d+(?:\\.\\d+)?|'[^']*'|\\\"[^\\\"]*\\\"|==|!=|<=|>=|<|>|&&|\\|\\||!|\\(|\\)|\\s)+$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly HashSet<string>? _knownAgents;
    private readonly HashSet<string>? _knownTeams;
    private readonly HashSet<string>? _knownGraphs;

    public GraphValidator(
        IReadOnlyCollection<string>? knownAgents = null,
        IReadOnlyCollection<string>? knownTeams = null,
        IReadOnlyCollection<string>? knownGraphs = null)
    {
        _knownAgents = knownAgents is null ? null : new HashSet<string>(knownAgents, StringComparer.OrdinalIgnoreCase);
        _knownTeams = knownTeams is null ? null : new HashSet<string>(knownTeams, StringComparer.OrdinalIgnoreCase);
        _knownGraphs = knownGraphs is null ? null : new HashSet<string>(knownGraphs, StringComparer.OrdinalIgnoreCase);
    }

    public GraphValidationResult Validate(GraphDefinition graph)
    {
        ArgumentNullException.ThrowIfNull(graph);
        var errors = new List<GraphValidationError>();
        if (graph.Version != 1)
        {
            errors.Add(Error("unsupported_version", "Graph version is not supported."));
        }
        var folderName = string.IsNullOrWhiteSpace(graph.FolderPath)
            ? graph.Name
            : Path.GetFileName(graph.FolderPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (!string.Equals(graph.Name, folderName, StringComparison.Ordinal))
        {
            errors.Add(Error("name_mismatch", "Graph name must match its folder name."));
        }

        var nodeIds = graph.Nodes.GroupBy(node => node.Id, StringComparer.Ordinal).Where(group => group.Count() > 1).Select(group => group.Key).ToArray();
        if (nodeIds.Length > 0)
        {
            errors.Add(Error("duplicate_id", "Node IDs must be unique.", nodeIds, Array.Empty<string>()));
        }
        var edgeIds = graph.Edges.GroupBy(edge => edge.Id, StringComparer.Ordinal).Where(group => group.Count() > 1).Select(group => group.Key).ToArray();
        if (edgeIds.Length > 0)
        {
            errors.Add(Error("duplicate_id", "Edge IDs must be unique.", Array.Empty<string>(), edgeIds));
        }

        var nodeSet = new HashSet<string>(graph.Nodes.Select(node => node.Id), StringComparer.Ordinal);
        foreach (var edge in graph.Edges)
        {
            if (!nodeSet.Contains(edge.From) || !nodeSet.Contains(edge.To))
            {
                errors.Add(Error("unknown_node_ref", $"Edge '{edge.Id}' references an unknown node.", [edge.From, edge.To], [edge.Id]));
            }
            if (!string.IsNullOrWhiteSpace(edge.Condition) && !ConditionTokenRegex.IsMatch(edge.Condition))
            {
                errors.Add(Error("invalid_condition", $"Edge '{edge.Id}' has an invalid condition.", [edge.From, edge.To], [edge.Id]));
            }
            ValidateReferences(edge.Condition, nodeSet, errors, Array.Empty<string>(), [edge.Id]);
        }

        foreach (var node in graph.Nodes)
        {
            if (!Enum.IsDefined(node.Kind))
            {
                errors.Add(Error("unknown_node_kind", $"Node '{node.Id}' has an unknown kind.", [node.Id], Array.Empty<string>()));
            }
            if (_knownAgents is not null && node.Agent is not null && !_knownAgents.Contains(node.Agent))
            {
                errors.Add(Error("unknown_definition_ref", $"Node '{node.Id}' references an unknown agent.", [node.Id], Array.Empty<string>()));
            }
            if (_knownTeams is not null && node.Team is not null && !_knownTeams.Contains(node.Team))
            {
                errors.Add(Error("unknown_definition_ref", $"Node '{node.Id}' references an unknown team.", [node.Id], Array.Empty<string>()));
            }
            if (_knownGraphs is not null && node.Graph is not null && !_knownGraphs.Contains(node.Graph))
            {
                errors.Add(Error("unknown_definition_ref", $"Node '{node.Id}' references an unknown graph.", [node.Id], Array.Empty<string>()));
            }
            ValidateReferences(node.Input, nodeSet, errors, [node.Id], Array.Empty<string>());
            ValidateReferences(node.Goal, nodeSet, errors, [node.Id], Array.Empty<string>());
            ValidateReferences(node.Summary, nodeSet, errors, [node.Id], Array.Empty<string>());

            var outgoing = graph.Edges.Where(edge => string.Equals(edge.From, node.Id, StringComparison.Ordinal)).ToArray();
            if (node.Kind == NodeKind.Branch && outgoing.Length > 0 && outgoing.All(edge => !string.IsNullOrWhiteSpace(edge.Condition)))
            {
                errors.Add(Error("missing_default_branch", $"Branch '{node.Id}' needs a default edge.", [node.Id], outgoing.Select(edge => edge.Id).ToArray()));
            }
            if (node.Kind == NodeKind.Join && node.JoinPolicy is null)
            {
                errors.Add(Error("missing_join_policy", $"Join '{node.Id}' needs a join policy.", [node.Id], Array.Empty<string>()));
            }
            if (node.Kind == NodeKind.Code && string.IsNullOrWhiteSpace(node.CodeFile))
            {
                errors.Add(Error("missing_code_file", $"Code '{node.Id}' needs a codeFile.", [node.Id], Array.Empty<string>()));
            }
            if (node.OnPartialFailure == PartialFailurePolicy.Alternate && string.IsNullOrWhiteSpace(node.Alternate))
            {
                errors.Add(Error("missing_alternate_target", $"Join '{node.Id}' needs an alternate target.", [node.Id], Array.Empty<string>()));
            }
            if (node.Kind == NodeKind.Loop)
            {
                if (node.Stop is null || (node.Stop.MaxIterations <= 0 && node.Stop.CostLimitUsd is null && node.Stop.TimeLimitSeconds is null && node.Stop.ScoreThreshold is null))
                {
                    errors.Add(Error("missing_stop_condition", $"Loop '{node.Id}' needs a stop condition.", [node.Id], Array.Empty<string>()));
                }
                if (node.Stop is not null && (node.Stop.MaxIterations is < 1 or > 100))
                {
                    errors.Add(Error("max_iterations_out_of_range", $"Loop '{node.Id}' has an invalid iteration limit.", [node.Id], Array.Empty<string>()));
                }
                if (node.Stop?.ScoreThreshold is < 0 or > 1)
                {
                    errors.Add(Error("score_threshold_out_of_range", $"Loop '{node.Id}' has an invalid score threshold.", [node.Id], Array.Empty<string>()));
                }
            }
        }

        var cycle = FindUndeclaredCycle(graph);
        if (cycle.Nodes.Count > 0)
        {
            errors.Add(Error("undeclared_cycle", "The graph contains a cycle that is not an explicit loop back.", cycle.Nodes, cycle.Edges));
        }
        var reachable = ReachableNodes(graph);
        var unreachable = graph.Nodes.Select(node => node.Id).Where(id => !reachable.Contains(id)).ToArray();
        if (unreachable.Length > 0)
        {
            errors.Add(Error("unreachable_node", "The graph contains unreachable nodes.", unreachable, Array.Empty<string>()));
        }
        ValidateSubgraphRecursion(graph, errors);
        return new GraphValidationResult(errors);
    }

    private static void ValidateReferences(
        string? text,
        IReadOnlySet<string> nodeSet,
        ICollection<GraphValidationError> errors,
        IReadOnlyList<string> nodeIds,
        IReadOnlyList<string> edgeIds)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }
        foreach (Match match in ReferenceRegex.Matches(text))
        {
            var reference = match.Groups["reference"].Value;
            if (reference is "mission.goal" or "mission.id" or "loop.iteration" or "loop.previous.output" or "loop.previous.score")
            {
                continue;
            }
            if (reference.StartsWith("nodes.", StringComparison.Ordinal))
            {
                var remaining = reference["nodes.".Length..];
                var nodeId = remaining.Split('.')[0];
                if (nodeSet.Contains(nodeId))
                {
                    continue;
                }
            }
            errors.Add(Error("unresolved_reference", $"Reference '{reference}' cannot be resolved.", nodeIds, edgeIds));
        }
    }

    private static HashSet<string> ReachableNodes(GraphDefinition graph)
    {
        var incoming = graph.Edges.Where(edge => !edge.LoopBack).Select(edge => edge.To).ToHashSet(StringComparer.Ordinal);
        var roots = graph.Nodes.Select(node => node.Id).Where(id => !incoming.Contains(id)).ToArray();
        var reachable = new HashSet<string>(StringComparer.Ordinal);
        var queue = new Queue<string>(roots.Length > 0 ? roots : graph.Nodes.Take(1).Select(node => node.Id));
        while (queue.Count > 0)
        {
            var id = queue.Dequeue();
            if (!reachable.Add(id))
            {
                continue;
            }
            foreach (var edge in graph.Edges.Where(edge => !edge.LoopBack && edge.From == id))
            {
                queue.Enqueue(edge.To);
            }
        }
        return reachable;
    }

    private static (IReadOnlyList<string> Nodes, IReadOnlyList<string> Edges) FindUndeclaredCycle(GraphDefinition graph)
    {
        var adjacency = graph.Edges.Where(edge => !edge.LoopBack)
            .GroupBy(edge => edge.From, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
        var state = new Dictionary<string, int>(StringComparer.Ordinal);
        var pathNodes = new List<string>();
        var pathEdges = new List<string>();
        foreach (var node in graph.Nodes)
        {
            if (state.ContainsKey(node.Id))
            {
                continue;
            }
            var result = Visit(node.Id);
            if (result.Nodes.Count > 0)
            {
                return result;
            }
        }
        return (Array.Empty<string>(), Array.Empty<string>());

        (IReadOnlyList<string> Nodes, IReadOnlyList<string> Edges) Visit(string nodeId)
        {
            state[nodeId] = 1;
            pathNodes.Add(nodeId);
            if (adjacency.TryGetValue(nodeId, out var edges))
            {
                foreach (var edge in edges)
                {
                    if (!state.TryGetValue(edge.To, out var nextState))
                    {
                        pathEdges.Add(edge.Id);
                        var nested = Visit(edge.To);
                        if (nested.Nodes.Count > 0)
                        {
                            return nested;
                        }
                        pathEdges.RemoveAt(pathEdges.Count - 1);
                    }
                    else if (nextState == 1)
                    {
                        var index = pathNodes.IndexOf(edge.To);
                        return (pathNodes.Skip(index).Append(edge.To).ToArray(), pathEdges.Skip(index).Append(edge.Id).ToArray());
                    }
                }
            }
            pathNodes.RemoveAt(pathNodes.Count - 1);
            state[nodeId] = 2;
            return (Array.Empty<string>(), Array.Empty<string>());
        }
    }

    private static void ValidateSubgraphRecursion(GraphDefinition graph, ICollection<GraphValidationError> errors)
    {
        var adjacency = graph.Nodes.Where(node => node.Kind == NodeKind.Subgraph && !string.IsNullOrWhiteSpace(node.Graph))
            .GroupBy(node => graph.Name, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Select(node => node.Graph!).ToArray(), StringComparer.Ordinal);
        if (adjacency.Count == 0)
        {
            return;
        }
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var active = new HashSet<string>(StringComparer.Ordinal);
        bool Visit(string graphName)
        {
            if (!active.Add(graphName))
            {
                return true;
            }
            if (seen.Add(graphName) && adjacency.TryGetValue(graphName, out var children))
            {
                foreach (var child in children)
                {
                    if (Visit(child))
                    {
                        return true;
                    }
                }
            }
            active.Remove(graphName);
            return false;
        }
        if (Visit(graph.Name))
        {
            errors.Add(Error("subgraph_recursion", "Subgraph calls contain recursion.", Array.Empty<string>(), Array.Empty<string>()));
        }
    }

    private static GraphValidationError Error(
        string code,
        string message,
        IReadOnlyList<string>? nodeIds = null,
        IReadOnlyList<string>? edgeIds = null)
        => new(code, message, nodeIds ?? Array.Empty<string>(), edgeIds ?? Array.Empty<string>());
}
