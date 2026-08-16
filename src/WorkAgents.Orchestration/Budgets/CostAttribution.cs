using WorkAgents.Core;
using WorkAgents.Core.Abstractions;

namespace WorkAgents.Orchestration.Budgets;

/// <summary>Attaches per-turn token usage to mission entities and persisted costs.</summary>
public sealed class CostAttribution
{
    private readonly ICostStore? _costs;

    public CostAttribution(ICostStore? costs = null)
    {
        _costs = costs;
    }

    public async Task<CostRecord> RecordTurnAsync(
        string agentName,
        string? modelName,
        string? missionId,
        string? agentInstanceId,
        string? nodeRunId,
        string? iterationId,
        long? inputTokens,
        long? outputTokens,
        CancellationToken ct = default)
    {
        var total = inputTokens.HasValue || outputTokens.HasValue
            ? (inputTokens ?? 0) + (outputTokens ?? 0)
            : (long?)null;
        var record = new CostRecord
        {
            CostRecordId = Guid.NewGuid().ToString("N"),
            AgentName = agentName,
            ModelName = modelName,
            MissionId = missionId,
            AgentInstanceId = agentInstanceId,
            NodeRunId = nodeRunId,
            IterationId = iterationId,
            InputTokens = inputTokens,
            OutputTokens = outputTokens,
            TotalTokens = total,
            EstimatedCostUsd = total is null ? null : total.Value * 0.000001,
        };
        if (_costs is not null)
        {
            await _costs.RecordAsync(record, ct);
        }
        return record;
    }
}
