using WorkAgents.Core.Graphs;

namespace WorkAgents.Core.Abstractions;

/// <summary>GraphVersion / NodeRun / EdgeTransit の永続化抽象。</summary>
public interface IGraphVersionStore
{
    /// <summary>同じ (graph_name, content_hash) が既にあれば既存を返し、新しい版は作らない。</summary>
    Task<GraphVersion> GetOrCreateVersionAsync(
        string graphName,
        string contentHash,
        Func<int, GraphVersion> factory,
        CancellationToken ct = default);

    Task<GraphVersion?> GetVersionAsync(string graphVersionId, CancellationToken ct = default);

    Task CreateNodeRunAsync(NodeRun nodeRun, CancellationToken ct = default);

    Task<NodeRun?> GetNodeRunAsync(string nodeRunId, CancellationToken ct = default);

    Task<IReadOnlyList<NodeRun>> ListNodeRunsAsync(string missionId, CancellationToken ct = default);

    Task SetNodeRunStateAsync(
        string nodeRunId,
        NodeRunState state,
        string? outputJson = null,
        string? error = null,
        CancellationToken ct = default);

    Task RecordEdgeTransitAsync(EdgeTransit transit, CancellationToken ct = default);

    Task<IReadOnlyList<EdgeTransit>> ListEdgeTransitsAsync(string missionId, CancellationToken ct = default);
}
