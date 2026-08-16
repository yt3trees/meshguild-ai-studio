using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using WorkAgents.Core.Abstractions;
using WorkAgents.Core.Graphs;
using WorkAgents.Core.Missions;
using WorkAgents.Orchestration.Loops;
using WorkAgents.Orchestration.Checkpoints;

namespace WorkAgents.Orchestration.Graph;

public delegate Task<string> GraphCodeHandler(GraphNode node, string input, CancellationToken ct);
public delegate Task<string> GraphNodeHandler(GraphNode node, string input, CancellationToken ct);

public sealed record GraphExecutionRequest
{
    public required string MissionId { get; init; }

    public required string Goal { get; init; }

    public required GraphDefinition Graph { get; init; }

    public string? WorkingDirectory { get; init; }

    public IReadOnlyDictionary<string, object?> Variables { get; init; } = new Dictionary<string, object?>();

    public GraphCodeHandler? CodeHandler { get; init; }

    public GraphNodeHandler? TeamHandler { get; init; }

    public GraphNodeHandler? ApprovalHandler { get; init; }

    public GraphNodeHandler? SubgraphHandler { get; init; }
}

public sealed record GraphExecutionResult(
    GraphVersion Version,
    IReadOnlyDictionary<string, string> Outputs,
    IReadOnlyList<NodeRun> NodeRuns,
    IReadOnlyList<EdgeTransit> EdgeTransits);

public sealed record NodeStateChangedEvent(string MissionId, NodeRun NodeRun);

public sealed record EdgeTransitedEvent(EdgeTransit Transit);

/// <summary>Deterministic graph interpreter with conditional edges, joins, loops, and version snapshots.</summary>
public sealed class GraphExecutor
{
    private readonly IAgentInvoker _invoker;
    private readonly IGraphVersionStore? _store;
    private readonly LoopExecutor? _loopExecutor;
    private readonly ExpressionEvaluator _expressions;
    private readonly CheckpointManager? _checkpoints;

    public event Func<NodeStateChangedEvent, Task>? NodeStateChanged;

    public event Func<EdgeTransitedEvent, Task>? EdgeTransited;

    public GraphExecutor(
        IAgentInvoker invoker,
        IGraphVersionStore? store = null,
        LoopExecutor? loopExecutor = null,
        ExpressionEvaluator? expressions = null,
        CheckpointManager? checkpoints = null)
    {
        ArgumentNullException.ThrowIfNull(invoker);
        _invoker = invoker;
        _store = store;
        _loopExecutor = loopExecutor;
        _expressions = expressions ?? new ExpressionEvaluator();
        _checkpoints = checkpoints;
    }

    public async Task<GraphExecutionResult> ExecuteAsync(GraphExecutionRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var validation = new GraphValidator().Validate(request.Graph);
        if (!validation.IsValid)
        {
            throw new InvalidOperationException($"Graph validation failed: {string.Join(", ", validation.Errors.Select(error => error.Code))}.");
        }

        var definition = JsonSerializer.Serialize(request.Graph);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(definition))).ToLowerInvariant()[..16];
        var version = _store is null
            ? new GraphVersion
            {
                GraphVersionId = Guid.NewGuid().ToString("N"),
                GraphName = request.Graph.Name,
                VersionNo = 1,
                ContentHash = hash,
                DefinitionYaml = definition,
            }
            : await _store.GetOrCreateVersionAsync(
                request.Graph.Name,
                hash,
                number => new GraphVersion
                {
                    GraphVersionId = Guid.NewGuid().ToString("N"),
                    GraphName = request.Graph.Name,
                    VersionNo = number,
                    ContentHash = hash,
                    DefinitionYaml = definition,
                },
                ct);

        var nodeRuns = request.Graph.Nodes.ToDictionary(
            node => node.Id,
            node => new NodeRun
            {
                NodeRunId = Guid.NewGuid().ToString("N"),
                MissionId = request.MissionId,
                NodeId = node.Id,
                NodeKind = node.Kind,
            },
            StringComparer.Ordinal);
        if (_store is not null)
        {
            foreach (var nodeRun in nodeRuns.Values)
            {
                await _store.CreateNodeRunAsync(nodeRun, ct);
            }
        }

        var outputs = new Dictionary<string, string>(StringComparer.Ordinal);
        var transits = new List<EdgeTransit>();
        var incoming = request.Graph.Edges.Where(edge => !edge.LoopBack)
            .GroupBy(edge => edge.To, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        var received = request.Graph.Nodes.ToDictionary(node => node.Id, _ => 0, StringComparer.Ordinal);
        var queued = new HashSet<string>(StringComparer.Ordinal);
        var queue = new Queue<GraphNode>(request.Graph.Nodes.Where(node => !incoming.ContainsKey(node.Id)));
        var variables = new Dictionary<string, object?>(request.Variables, StringComparer.Ordinal)
        {
            ["mission.goal"] = request.Goal,
            ["mission.id"] = request.MissionId,
        };

        while (queue.Count > 0)
        {
            var node = queue.Dequeue();
            if (!queued.Add(node.Id) && node.Kind != NodeKind.Loop)
            {
                continue;
            }
            var nodeRun = nodeRuns[node.Id];
            nodeRun = await SetStateAsync(nodeRun, NodeRunState.Running, ct);
            nodeRuns[node.Id] = nodeRun;
            var input = Render(node.Input ?? node.Goal ?? request.Goal, variables, outputs);
            try
            {
                var output = await ExecuteNodeAsync(node, input, request, ct);
                outputs[node.Id] = output;
                variables[$"nodes.{node.Id}.output"] = output;
                nodeRun = await SetStateAsync(nodeRun, NodeRunState.Succeeded, ct, output);
                nodeRuns[node.Id] = nodeRun;
                if (_checkpoints is not null)
                {
                    await _checkpoints.SaveAsync(
                        request.MissionId,
                        CheckpointBoundaryKind.Node,
                        JsonSerializer.Serialize(new { nodeId = node.Id, output }),
                        0,
                        workspacePath: request.WorkingDirectory,
                        nodeRunId: nodeRun.NodeRunId,
                        ct: ct);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                nodeRun = await SetStateAsync(nodeRun, NodeRunState.Failed, ct, error: "Graph node execution failed.");
                nodeRuns[node.Id] = nodeRun;
                if (node.Kind == NodeKind.Join && node.OnPartialFailure == PartialFailurePolicy.Continue)
                {
                    continue;
                }
                throw new InvalidOperationException($"Graph node '{node.Id}' failed.", ex);
            }

            var outgoing = request.Graph.Edges.Where(edge => edge.From == node.Id && !edge.LoopBack).ToArray();
            var selected = SelectEdges(node, outgoing, variables, outputs);
            foreach (var edge in selected)
            {
                received[edge.To]++;
                var transit = new EdgeTransit
                {
                    EdgeTransitId = Guid.NewGuid().ToString("N"),
                    MissionId = request.MissionId,
                    EdgeId = edge.Id,
                    FromNodeRunId = nodeRun.NodeRunId,
                    ToNodeRunId = nodeRuns[edge.To].NodeRunId,
                    ConditionResult = edge.Condition is null ? null : "true",
                };
                transits.Add(transit);
                await NotifyAsync(EdgeTransited, new EdgeTransitedEvent(transit));
                if (_store is not null)
                {
                    await _store.RecordEdgeTransitAsync(transit, ct);
                }

                var target = request.Graph.Nodes.Single(candidate => candidate.Id == edge.To);
                var required = target.JoinPolicy == JoinPolicy.Any ? 1 : incoming.GetValueOrDefault(target.Id, 1);
                if (received[target.Id] >= required)
                {
                    queue.Enqueue(target);
                }
            }
        }

        foreach (var nodeRun in nodeRuns.Values.Where(nodeRun => nodeRun.State == NodeRunState.Pending))
        {
            var updated = await SetStateAsync(nodeRun, NodeRunState.Unreached, ct);
            nodeRuns[updated.NodeId] = updated;
        }
        return new GraphExecutionResult(version, outputs, nodeRuns.Values.ToArray(), transits);
    }

    private async Task<string> ExecuteNodeAsync(GraphNode node, string input, GraphExecutionRequest request, CancellationToken ct)
    {
        return node.Kind switch
        {
            NodeKind.Agent => (await _invoker.InvokeAsync(new AgentInvocation
            {
                AgentName = node.Agent ?? throw new InvalidOperationException($"Agent node '{node.Id}' has no agent."),
                Context = input,
                WorkingDirectory = request.WorkingDirectory,
                MissionId = request.MissionId,
                ThreadId = $"graph:{request.MissionId}:{node.Id}",
            }, ct)).Utterance,
            NodeKind.Code => request.CodeHandler is null
                ? input
                : await request.CodeHandler(node, input, ct),
            NodeKind.Branch or NodeKind.Parallel or NodeKind.Join => input,
            NodeKind.Approval => request.ApprovalHandler is null ? "approval_pending" : await request.ApprovalHandler(node, input, ct),
            NodeKind.Team => request.TeamHandler is null ? input : await request.TeamHandler(node, input, ct),
            NodeKind.Subgraph => request.SubgraphHandler is null ? input : await request.SubgraphHandler(node, input, ct),
            NodeKind.Loop => await ExecuteLoopNodeAsync(node, input, request, ct),
            _ => throw new InvalidOperationException($"Unsupported graph node kind '{node.Kind}'."),
        };
    }

    private async Task<string> ExecuteLoopNodeAsync(GraphNode node, string input, GraphExecutionRequest request, CancellationToken ct)
    {
        if (_loopExecutor is null || string.IsNullOrWhiteSpace(node.Agent))
        {
            return input;
        }
        var result = await _loopExecutor.ExecuteAsync(new LoopExecutionRequest
        {
            MissionId = request.MissionId,
            NodeRunId = Guid.NewGuid().ToString("N"),
            AgentName = node.Agent,
            InitialInput = input,
            WorkingDirectory = request.WorkingDirectory,
            MaxIterations = node.Stop?.MaxIterations ?? 10,
            CostLimitUsd = node.Stop?.CostLimitUsd,
            TimeLimitSeconds = node.Stop?.TimeLimitSeconds,
            ScoreThreshold = node.Stop?.ScoreThreshold ?? 1,
        }, ct);
        return result.BestOutput ?? input;
    }

    private IReadOnlyList<GraphEdge> SelectEdges(
        GraphNode node,
        IReadOnlyList<GraphEdge> edges,
        IReadOnlyDictionary<string, object?> variables,
        IReadOnlyDictionary<string, string> outputs)
    {
        if (node.Kind != NodeKind.Branch)
        {
            return edges;
        }
        var selected = edges.Where(edge =>
        {
            if (string.IsNullOrWhiteSpace(edge.Condition))
            {
                return true;
            }
            return _expressions.EvaluateBoolean(edge.Condition, variables);
        }).ToArray();
        return selected.Length > 0 ? selected.Take(1).ToArray() : edges.Where(edge => edge.Condition is null).Take(1).ToArray();
    }

    private async Task<NodeRun> SetStateAsync(NodeRun nodeRun, NodeRunState state, CancellationToken ct, string? output = null, string? error = null)
    {
        if (nodeRun.State != state)
        {
            NodeRunStateMachine.EnsureTransition(nodeRun.State, state);
        }
        var updated = nodeRun with
        {
            State = state,
            OutputJson = output ?? nodeRun.OutputJson,
            Error = error ?? nodeRun.Error,
            StartedAt = state == NodeRunState.Running ? nodeRun.StartedAt ?? DateTimeOffset.UtcNow : nodeRun.StartedAt,
            CompletedAt = state is NodeRunState.Succeeded or NodeRunState.Failed or NodeRunState.Skipped or NodeRunState.Unreached
                ? nodeRun.CompletedAt ?? DateTimeOffset.UtcNow
                : nodeRun.CompletedAt,
        };
        if (_store is not null)
        {
            await _store.SetNodeRunStateAsync(nodeRun.NodeRunId, state, output, error, ct);
        }
        if (NodeStateChanged is not null)
        {
            foreach (var handler in NodeStateChanged.GetInvocationList().Cast<Func<NodeStateChangedEvent, Task>>())
            {
                try { await handler(new NodeStateChangedEvent(updated.MissionId, updated)); } catch { }
            }
        }
        return updated;
    }

    private static async Task NotifyAsync<T>(Func<T, Task>? handlers, T value)
    {
        if (handlers is null) return;
        foreach (var handler in handlers.GetInvocationList().Cast<Func<T, Task>>())
        {
            try { await handler(value); } catch { }
        }
    }

    private static string Render(string template, IReadOnlyDictionary<string, object?> variables, IReadOnlyDictionary<string, string> outputs)
    {
        if (string.IsNullOrEmpty(template))
        {
            return string.Empty;
        }
        foreach (var pair in variables)
        {
            template = template.Replace("${" + pair.Key + "}", pair.Value?.ToString() ?? string.Empty, StringComparison.Ordinal);
        }
        foreach (var pair in outputs)
        {
            template = template.Replace("${nodes." + pair.Key + ".output}", pair.Value, StringComparison.Ordinal);
        }
        return template;
    }
}
