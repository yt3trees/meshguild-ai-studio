using WorkAgents.Core;
using WorkAgents.Core.Abstractions;

namespace WorkAgents.Orchestration.Replay;

public sealed record AgentCostSummary(string AgentName, long Tokens, double EstimatedCostUsd, int Records);

public sealed record IterationCostSummary(string IterationId, long Tokens, double EstimatedCostUsd, int Records);

public sealed record MissionReport(
    IReadOnlyList<AgentCostSummary> ByAgent,
    IReadOnlyList<IterationCostSummary> ByIteration,
    IReadOnlyList<string> NodeIds);

/// <summary>Builds replay aggregates from persisted mission dimensions.</summary>
public sealed class MissionReportBuilder
{
    private readonly ICostStore _costs;
    private readonly IGraphVersionStore? _graphs;

    public MissionReportBuilder(ICostStore costs, IGraphVersionStore? graphs = null)
    {
        ArgumentNullException.ThrowIfNull(costs);
        _costs = costs;
        _graphs = graphs;
    }

    public async Task<MissionReport> BuildAsync(string missionId, DateTimeOffset? since = null, CancellationToken ct = default)
    {
        var records = (await _costs.ListAsync(since ?? DateTimeOffset.MinValue, ct))
            .Where(record => string.Equals(record.MissionId, missionId, StringComparison.Ordinal))
            .ToArray();
        var byAgent = records.GroupBy(record => record.AgentName, StringComparer.OrdinalIgnoreCase)
            .Select(group => new AgentCostSummary(
                group.Key,
                group.Sum(record => record.TotalTokens ?? 0),
                group.Sum(record => record.EstimatedCostUsd ?? 0),
                group.Count()))
            .OrderBy(summary => summary.AgentName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var byIteration = records.Where(record => record.IterationId is not null)
            .GroupBy(record => record.IterationId!, StringComparer.Ordinal)
            .Select(group => new IterationCostSummary(
                group.Key,
                group.Sum(record => record.TotalTokens ?? 0),
                group.Sum(record => record.EstimatedCostUsd ?? 0),
                group.Count()))
            .OrderBy(summary => summary.IterationId, StringComparer.Ordinal)
            .ToArray();
        return new MissionReport(byAgent, byIteration, Array.Empty<string>());
    }
}
