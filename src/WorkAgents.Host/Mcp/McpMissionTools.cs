using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.ComponentModel;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using Microsoft.Extensions.Options;
using WorkAgents.Core.Abstractions;
using WorkAgents.Core.Missions;
using WorkAgents.Core.Graphs;
using WorkAgents.Core.Teams;
using WorkAgents.Orchestration;

namespace WorkAgents.Host.Mcp;

public sealed record McpMissionBudgetInput(
    double? CostLimitUsd = null,
    int? TimeLimitSeconds = null,
    int? MaxIterations = null,
    int? MaxConcurrentAgents = null);

public sealed record McpMissionSubmissionResult(
    string MissionId,
    string Status,
    string TargetKind,
    string TargetName,
    string PollResourceUri,
    bool Replayed);

public sealed record McpMissionSnapshot(
    string MissionId,
    string TargetKind,
    string TargetName,
    string Status,
    string? Outcome,
    string? StopReason,
    DateTimeOffset CreatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    bool AwaitingApproval,
    string? Error,
    string PollResourceUri);

[McpServerToolType]
public sealed class McpMissionTools
{
    private readonly MissionEngine _engine;
    private readonly IMissionStore _missions;
    private readonly IMcpSubmissionStore _submissions;
    private readonly IReadOnlyList<TeamDefinition> _teams;
    private readonly IReadOnlyList<GraphDefinition> _graphs;
    private readonly McpRequestValidator _validator;
    private readonly McpAuditLogger _audit;
    private readonly McpOptions _options;

    public McpMissionTools(
        MissionEngine engine,
        IMissionStore missions,
        IMcpSubmissionStore submissions,
        IReadOnlyList<TeamDefinition> teams,
        IReadOnlyList<GraphDefinition> graphs,
        McpRequestValidator validator,
        McpAuditLogger audit,
        IOptions<McpOptions> options)
    {
        _engine = engine;
        _missions = missions;
        _submissions = submissions;
        _teams = teams;
        _graphs = graphs;
        _validator = validator;
        _audit = audit;
        _options = options.Value;
    }

    [McpServerTool, Description("Submit a Team or Graph mission and return an asynchronous Mission handle.")]
    public async Task<McpMissionSubmissionResult> workagents_submit_mission(
        [Description("Stable client-generated idempotency key for this logical submission.")] string requestKey,
        [Description("The goal to execute.")] string goal,
        [Description("Team or Graph.")] string targetKind,
        [Description("The resolved Team or Graph name.")] string targetName,
        McpMissionBudgetInput? budget = null,
        CancellationToken cancellationToken = default)
    {
        ValidateText(requestKey, "requestKey", 256);
        ValidateText(goal, "goal", 32_000);
        ValidateText(targetName, "targetName", 256);

        if (!Enum.TryParse<MissionTargetKind>(targetKind, ignoreCase: true, out var parsedTargetKind))
        {
            throw Error(McpErrorMapper.InvalidInput("targetKind must be Team or Graph."));
        }

        if (parsedTargetKind == MissionTargetKind.Team
            && !_teams.Any(team => string.Equals(team.Name, targetName, StringComparison.OrdinalIgnoreCase)))
        {
            throw Error(McpErrorMapper.UnknownTarget($"Team '{targetName}' is not available."));
        }

        if (parsedTargetKind == MissionTargetKind.Graph
            && !_graphs.Any(graph => string.Equals(graph.Name, targetName, StringComparison.OrdinalIgnoreCase)))
        {
            throw Error(McpErrorMapper.UnknownTarget($"Graph '{targetName}' is not available."));
        }

        ValidateBudget(budget);
        var requestHash = ComputeRequestHash(goal, parsedTargetKind, targetName, budget);
        var existing = await _submissions.GetAsync(requestKey, cancellationToken);
        if (existing is not null)
        {
            if (!string.Equals(existing.RequestHash, requestHash, StringComparison.Ordinal))
            {
                throw Error(new McpToolError("idempotency_conflict", "requestKey was already used with different input.", "Generate a new requestKey."));
            }

            var replayedMission = await _missions.GetAsync(existing.MissionId, cancellationToken)
                ?? throw Error(new McpToolError("mission_not_found", "The Mission recorded for requestKey is no longer available."));
            return ToSubmissionResult(replayedMission, replayed: true);
        }

        var mission = new Mission
        {
            MissionId = Guid.NewGuid().ToString("N"),
            Goal = goal.Trim(),
            TargetKind = parsedTargetKind,
            TargetName = targetName.Trim(),
            TeamName = parsedTargetKind == MissionTargetKind.Team ? targetName.Trim() : null,
            TriggerKind = MissionTriggerKind.Manual,
        };

        var claimed = await _submissions.TryCreateAsync(new McpSubmission
        {
            RequestKey = requestKey.Trim(),
            RequestHash = requestHash,
            MissionId = mission.MissionId,
        }, cancellationToken);
        if (!claimed)
        {
            var raced = await _submissions.GetAsync(requestKey, cancellationToken)
                ?? throw Error(new McpToolError("idempotency_conflict", "requestKey could not be claimed safely.", "Retry with a new requestKey."));
            if (!string.Equals(raced.RequestHash, requestHash, StringComparison.Ordinal))
            {
                throw Error(new McpToolError("idempotency_conflict", "requestKey was already used with different input.", "Generate a new requestKey."));
            }

            var racedMission = await _missions.GetAsync(raced.MissionId, cancellationToken)
                ?? throw Error(new McpToolError("mission_not_found", "The Mission recorded for requestKey is no longer available."));
            return ToSubmissionResult(racedMission, replayed: true);
        }

        try
        {
            if (budget is not null)
            {
                await _engine.ConfigureBudgetAsync(mission.MissionId, new Budget
                {
                    MissionId = mission.MissionId,
                    CostLimitUsd = budget.CostLimitUsd,
                    TimeLimitSeconds = budget.TimeLimitSeconds,
                    MaxIterations = budget.MaxIterations,
                    MaxConcurrentAgents = budget.MaxConcurrentAgents,
                }, cancellationToken);
            }

            var accepted = await _engine.SubmitAsync(mission, cancellationToken);
            _audit.Record(new McpAuditEvent(
                "mcp.mission.submitted",
                "2026-07-28",
                "workagents_submit_mission",
                "accepted",
                TargetKind: accepted.TargetKind.ToString(),
                TargetName: accepted.TargetName,
                MissionId: accepted.MissionId));
            return ToSubmissionResult(accepted, replayed: false);
        }
        catch
        {
            // The idempotency row intentionally remains a tombstone if the mission store accepted
            // the request before a later step failed. A retry cannot silently create a duplicate.
            throw;
        }
    }

    [McpServerTool, Description("Read the safe current state of a WorkAgents Mission.")]
    public async Task<McpMissionSnapshot> workagents_get_mission(
        [Description("Opaque Mission identifier returned by submit.")] string missionId,
        CancellationToken cancellationToken = default)
    {
        ValidateText(missionId, "missionId", 128);
        var mission = await _missions.GetAsync(missionId.Trim(), cancellationToken)
            ?? throw Error(McpErrorMapper.NotFound("Mission was not found."));
        return ToSnapshot(mission);
    }

    [McpServerTool, Description("Request explicit cancellation of a running or waiting WorkAgents Mission.")]
    public async Task<McpMissionSnapshot> workagents_cancel_mission(
        [Description("Opaque Mission identifier returned by submit.")] string missionId,
        [Description("Optional safe reason for the cancellation.")] string? reason = null,
        CancellationToken cancellationToken = default)
    {
        ValidateText(missionId, "missionId", 128);
        var mission = await _missions.GetAsync(missionId.Trim(), cancellationToken)
            ?? throw Error(McpErrorMapper.NotFound("Mission was not found."));
        if (mission.Status is MissionStatus.Succeeded or MissionStatus.NotConverged or MissionStatus.Failed or MissionStatus.Aborted)
        {
            throw Error(new McpToolError("mission_not_cancellable", "Mission is already in a terminal state.", "Poll the Mission instead."));
        }

        await _engine.AbortAsync(mission.MissionId, cancellationToken);
        var aborted = await _missions.GetAsync(mission.MissionId, cancellationToken)
            ?? throw Error(McpErrorMapper.NotFound("Mission was not found after cancellation."));
        _audit.Record(new McpAuditEvent(
            "mcp.mission.cancelled",
            "2026-07-28",
            "workagents_cancel_mission",
            "accepted",
            TargetName: aborted.TargetName,
            MissionId: aborted.MissionId));
        return ToSnapshot(aborted);
    }

    private static McpMissionSubmissionResult ToSubmissionResult(Mission mission, bool replayed)
        => new(
            mission.MissionId,
            mission.Status.ToString().ToLowerInvariant(),
            mission.TargetKind.ToString(),
            mission.TargetName,
            $"workagents://missions/{mission.MissionId}",
            replayed);

    public static McpMissionSnapshot ToSnapshot(Mission mission)
        => new(
            mission.MissionId,
            mission.TargetKind.ToString(),
            mission.TargetName,
            mission.Status.ToString().ToLowerInvariant(),
            mission.Outcome?.ToString().ToLowerInvariant(),
            mission.StopReason?.ToString().ToLowerInvariant(),
            mission.CreatedAt,
            mission.StartedAt,
            mission.CompletedAt,
            mission.Status == MissionStatus.AwaitingApproval,
            McpResponseProjector.SafeText(mission.Error, 1000),
            $"workagents://missions/{mission.MissionId}");

    private static void ValidateText(string? value, string name, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Trim().Length > maxLength)
        {
            throw Error(McpErrorMapper.InvalidInput($"{name} is required and must be at most {maxLength} characters."));
        }
    }

    private static void ValidateBudget(McpMissionBudgetInput? budget)
    {
        if (budget is null)
        {
            return;
        }

        if (budget.CostLimitUsd is < 0
            || budget.TimeLimitSeconds is < 1
            || budget.MaxIterations is < 1 or > 100
            || budget.MaxConcurrentAgents is < 1)
        {
            throw Error(McpErrorMapper.InvalidInput("budget contains an invalid limit."));
        }
    }

    private static string ComputeRequestHash(
        string goal,
        MissionTargetKind targetKind,
        string targetName,
        McpMissionBudgetInput? budget)
    {
        var canonical = JsonSerializer.Serialize(new
        {
            goal = goal.Trim(),
            targetKind = targetKind.ToString(),
            targetName = targetName.Trim(),
            budget,
        });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }

    private static McpException Error(McpToolError error)
        => new($"[{error.Code}] {error.Message} {error.NextAction}");
}
