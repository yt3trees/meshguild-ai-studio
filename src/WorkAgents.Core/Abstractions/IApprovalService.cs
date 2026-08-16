using WorkAgents.Core;

namespace WorkAgents.Core.Abstractions;

/// <summary>承認要求の作成、待機、決定をBackgroundServiceとWebUIの間で仲介する。</summary>
public interface IApprovalService
{
    Task<ApprovalRequest> RequestAsync(
        string runId,
        string tool,
        string argsSummary,
        TimeSpan timeout,
        CancellationToken ct = default);

    /// <summary>title 付き承認要求(M3.5 workflow approve / デスクトップ通知表示用)。</summary>
    Task<ApprovalRequest> RequestAsync(
        string runId,
        string tool,
        string argsSummary,
        TimeSpan timeout,
        string? title,
        CancellationToken ct = default);

    Task<bool> DecideAsync(
        string approvalId,
        ApprovalStatus status,
        string decidedBy,
        string? reason = null,
        CancellationToken ct = default);

    Task<ApprovalRequest> RequestMissionAsync(
        string missionId,
        string agentInstanceId,
        string tool,
        string argsSummary,
        TimeSpan timeout,
        string? title = null,
        string? nodeRunId = null,
        string? iterationId = null,
        CancellationToken ct = default);
}
