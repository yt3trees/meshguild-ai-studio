using System.ComponentModel;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using WorkAgents.Core.Abstractions;
using WorkAgents.Core.Graphs;
using WorkAgents.Core.Loops;
using WorkAgents.Core.Missions;

namespace WorkAgents.Host.Mcp;

public sealed record McpNodeObservation(
    string NodeId,
    string Kind,
    string State,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    string? Error);

public sealed record McpEdgeObservation(
    string EdgeId,
    string From,
    string To,
    string? ConditionResult,
    DateTimeOffset TransitedAt);

public sealed record McpLoopObservation(
    string LoopRunId,
    int MaxIterations,
    int CurrentIteration,
    string? StopReason,
    double? ScoreThreshold,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt);

public sealed record McpGraphObservation(
    string MissionId,
    string GraphName,
    DateTimeOffset ObservedAt,
    int? GraphVersionNo,
    string? ContentHash,
    IReadOnlyList<McpNodeObservation> Nodes,
    IReadOnlyList<McpEdgeObservation> Edges,
    IReadOnlyList<McpLoopObservation> Loops,
    bool IsPartial,
    int? NextOffset);

[McpServerToolType]
public sealed class McpObservationTools
{
    private readonly IMissionStore _missions;
    private readonly IGraphVersionStore _graphs;
    private readonly ILoopStore _loops;
    private readonly McpRequestValidator _validator;

    public McpObservationTools(
        IMissionStore missions,
        IGraphVersionStore graphs,
        ILoopStore loops,
        McpRequestValidator validator)
    {
        _missions = missions;
        _graphs = graphs;
        _loops = loops;
        _validator = validator;
    }

    [McpServerTool, Description("Read bounded Graph, node, edge, and loop execution state for a Mission.")]
    public Task<McpGraphObservation> workagents_get_graph(
        [Description("Opaque Mission identifier returned by submit.")] string missionId,
        [Description("Numeric offset cursor for node observations.")] int offset = 0,
        CancellationToken cancellationToken = default)
        => BuildAsync(missionId, offset, cancellationToken);

    public async Task<McpGraphObservation> BuildAsync(string missionId, int offset, CancellationToken ct = default)
    {
        if (!McpResourceAccessPolicy.IsSafeIdentifier(missionId))
        {
            throw new McpException("[invalid_input] missionId is invalid.");
        }

        var mission = await _missions.GetAsync(missionId, ct)
            ?? throw new McpException("[mission_not_found] Mission was not found.");
        var observedAt = DateTimeOffset.UtcNow;
        var graphVersion = string.IsNullOrWhiteSpace(mission.GraphVersionId)
            ? null
            : await _graphs.GetVersionAsync(mission.GraphVersionId, ct);
        var allNodes = await _graphs.ListNodeRunsAsync(missionId, ct);
        var allEdges = await _graphs.ListEdgeTransitsAsync(missionId, ct);
        var loops = await _loops.ListLoopRunsAsync(missionId, ct);
        var page = McpResponseProjector.Page(
            allNodes.OrderBy(node => node.NodeId, StringComparer.Ordinal).ThenBy(node => node.NodeRunId, StringComparer.Ordinal),
            Math.Max(0, offset),
            _validator.ClampPageSize(null),
            out var nextOffset);
        var nodeByRunId = allNodes.ToDictionary(node => node.NodeRunId, node => node.NodeId, StringComparer.Ordinal);

        var nodeSnapshots = page.Select(node => new McpNodeObservation(
            node.NodeId,
            node.NodeKind.ToString(),
            node.State.ToString().ToLowerInvariant(),
            node.StartedAt,
            node.CompletedAt,
            McpResponseProjector.SafeText(node.Error, 500))).ToArray();
        var edgeSnapshots = allEdges
            .OrderBy(edge => edge.TransitedAt)
            .Select(edge => new McpEdgeObservation(
                edge.EdgeId,
                nodeByRunId.GetValueOrDefault(edge.FromNodeRunId, "unknown"),
                nodeByRunId.GetValueOrDefault(edge.ToNodeRunId, "unknown"),
                McpResponseProjector.SafeText(edge.ConditionResult, 100),
                edge.TransitedAt))
            .ToArray();
        var loopSnapshots = new List<McpLoopObservation>();
        foreach (var loop in loops.OrderBy(item => item.LoopRunId, StringComparer.Ordinal))
        {
            var iterations = await _loops.ListIterationsAsync(loop.LoopRunId, includeDiscarded: false, ct);
            loopSnapshots.Add(new McpLoopObservation(
                loop.LoopRunId,
                loop.MaxIterations,
                iterations.Count == 0 ? 0 : iterations.Max(iteration => iteration.IterationNo),
                loop.StopReason?.ToString().ToLowerInvariant(),
                loop.ScoreThreshold,
                loop.StartedAt,
                loop.CompletedAt));
        }

        var partial = mission.Status is not (MissionStatus.Succeeded or MissionStatus.NotConverged or MissionStatus.Failed or MissionStatus.Aborted)
            || allNodes.Any(node => node.State is NodeRunState.Pending or NodeRunState.Running or NodeRunState.Waiting);
        return new McpGraphObservation(
            mission.MissionId,
            mission.TargetName,
            observedAt,
            graphVersion?.VersionNo,
            graphVersion?.ContentHash,
            nodeSnapshots,
            edgeSnapshots,
            loopSnapshots,
            partial,
            nextOffset);
    }
}
