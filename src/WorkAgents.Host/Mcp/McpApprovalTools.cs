using System.ComponentModel;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using WorkAgents.Core;
using WorkAgents.Core.Abstractions;

namespace WorkAgents.Host.Mcp;

public sealed record McpApprovalReference(
    string ApprovalId,
    string? MissionId,
    string Tool,
    string ArgsSummary,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    string NextAction);

[McpServerToolType]
public sealed class McpApprovalTools
{
    private readonly IApprovalStore _approvals;
    private readonly ISecretRedactor? _redactor;

    public McpApprovalTools(IApprovalStore approvals, ISecretRedactor? redactor = null)
    {
        _approvals = approvals;
        _redactor = redactor;
    }

    [McpServerTool, Description("Read a pending or decided approval reference without making an approval decision.")]
    public async Task<McpApprovalReference> workagents_get_approval(
        [Description("Mission identifier used to scope the approval.")] string missionId,
        [Description("Optional approval identifier.")] string? approvalId = null,
        CancellationToken cancellationToken = default)
        => await GetAsync(missionId, approvalId, cancellationToken);

    public async Task<McpApprovalReference> GetAsync(
        string missionId,
        string? approvalId = null,
        CancellationToken ct = default)
    {
        if (!McpResourceAccessPolicy.IsSafeIdentifier(missionId))
        {
            throw new McpException("[mission_not_found] Mission was not found.");
        }

        ApprovalRequest? approval = string.IsNullOrWhiteSpace(approvalId)
            ? (await _approvals.ListPendingAsync(ct: ct)).FirstOrDefault(item => string.Equals(item.MissionId, missionId, StringComparison.Ordinal))
            : await _approvals.GetAsync(approvalId, ct);
        if (approval is null || !string.Equals(approval.MissionId, missionId, StringComparison.Ordinal))
        {
            throw new McpException("[approval_not_found] Approval was not found for the Mission.");
        }

        var expired = approval.IsExpired(DateTimeOffset.UtcNow);
        var status = expired ? "expired" : approval.Status.ToString().ToLowerInvariant();
        var nextAction = status == "pending" ? "open_approval_ui" : "wait";
        var summary = _redactor is null
            ? approval.ArgsSummary
            : await _redactor.RedactAsync(approval.ArgsSummary, ct);
        return new McpApprovalReference(
            approval.ApprovalId,
            approval.MissionId,
            McpResponseProjector.SafeText(approval.Tool, 200) ?? "unknown",
            McpResponseProjector.SafeText(summary, 1000) ?? "",
            status,
            approval.CreatedAt,
            approval.ExpiresAt,
            nextAction);
    }
}
