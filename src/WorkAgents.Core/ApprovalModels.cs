namespace WorkAgents.Core;

/// <summary>承認インボックスに表示し、永続化する承認要求。</summary>
public sealed record ApprovalRequest
{
    public required string ApprovalId { get; init; }

    public required string RunId { get; init; }

    public required string Tool { get; init; }

    public required string ArgsSummary { get; init; }

    /// <summary>承認要求の表示タイトル(5.13.1)。ワークフロー approve ステップの title、shell 由来は Tool 名にフォールバック。</summary>
    public string Title { get; init; } = "";

    public ApprovalStatus Status { get; init; } = ApprovalStatus.Pending;

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    public required DateTimeOffset ExpiresAt { get; init; }

    public string? DecidedBy { get; init; }

    public DateTimeOffset? DecidedAt { get; init; }

    public string? DecisionReason { get; init; }

    /// <summary>ミッション経路の承認要求のとき、対象ミッション (R-11)。</summary>
    public string? MissionId { get; init; }

    /// <summary>ミッション経路の承認要求のとき、停止対象のエージェントインスタンス (FR-018)。</summary>
    public string? AgentInstanceId { get; init; }

    /// <summary>対応するノード実行。</summary>
    public string? NodeRunId { get; init; }

    /// <summary>対応する反復。</summary>
    public string? IterationId { get; init; }

    public bool IsExpired(DateTimeOffset now) => Status == ApprovalStatus.Pending && now >= ExpiresAt;

    public static ApprovalRequest Create(
        string runId,
        string tool,
        string argsSummary,
        TimeSpan timeout,
        DateTimeOffset? now = null,
        string? approvalId = null,
        string? title = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        ArgumentException.ThrowIfNullOrWhiteSpace(tool);
        ArgumentNullException.ThrowIfNull(argsSummary);
        if (timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout), timeout, "Approval timeout must be positive.");
        }

        var createdAt = now ?? DateTimeOffset.UtcNow;
        return new ApprovalRequest
        {
            ApprovalId = string.IsNullOrWhiteSpace(approvalId) ? Guid.NewGuid().ToString("N") : approvalId,
            RunId = runId,
            Tool = tool,
            ArgsSummary = argsSummary,
            Title = string.IsNullOrWhiteSpace(title) ? tool : title,
            CreatedAt = createdAt,
            ExpiresAt = createdAt.Add(timeout),
        };
    }
}

/// <summary>承認要求で許可される状態遷移を一元化する。</summary>
public static class ApprovalStatusMachine
{
    public static bool CanTransition(ApprovalStatus from, ApprovalStatus to)
        => from == ApprovalStatus.Pending && to is ApprovalStatus.Approved or ApprovalStatus.Rejected;

    public static void EnsureTransition(ApprovalStatus from, ApprovalStatus to)
    {
        if (!CanTransition(from, to))
        {
            throw new InvalidOperationException($"Invalid approval status transition: {from} -> {to}.");
        }
    }
}