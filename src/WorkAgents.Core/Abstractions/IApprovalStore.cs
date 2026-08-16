using WorkAgents.Core;

namespace WorkAgents.Core.Abstractions;

/// <summary>承認要求の永続化。LocalはSQLite、AzureはCosmos実装に差し替える。</summary>
public interface IApprovalStore
{
    Task CreateAsync(ApprovalRequest request, CancellationToken ct = default);

    Task<ApprovalRequest?> GetAsync(string approvalId, CancellationToken ct = default);

    Task<IReadOnlyList<ApprovalRequest>> ListPendingAsync(
        string? runId = null,
        CancellationToken ct = default);

    Task<bool> TryDecideAsync(
        string approvalId,
        ApprovalStatus status,
        string decidedBy,
        string? reason = null,
        DateTimeOffset? decidedAt = null,
        CancellationToken ct = default);
}