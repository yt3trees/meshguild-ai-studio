namespace WorkAgents.Core;

/// <summary>
/// 実行プロファイル。Local(M0〜M6) / Azure(M7〜)。
/// 設定 <c>Profile</c> セクションで切替。ドメイン層・エージェント・WebUI はこれを意識しない。
/// </summary>
public enum Profile
{
    Local,
    Azure,
}

/// <summary>Run のライフサイクル状態(5.6)。</summary>
public enum RunStatus
{
    Queued,
    Running,
    AwaitingApproval,
    Succeeded,
    Failed,
    Aborted,
}

/// <summary>thread単位で保存されるMAFセッションの状態。</summary>
public sealed record SessionRecord
{
    public required string ThreadId { get; init; }

    public required string AgentName { get; init; }

    public required string SerializedState { get; init; }

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAt { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>非同期 run の受付内容。</summary>
public sealed record RunRequest(string AgentName, string UserMessage, string? ThreadId = null);

/// <summary>キュー投入後に永続化される run の状態と実行結果。</summary>
public sealed record RunRecord
{
    public required string RunId { get; init; }

    public required string AgentName { get; init; }

    public required string UserMessage { get; init; }

    public string? ThreadId { get; init; }

    public RunStatus Status { get; init; } = RunStatus.Queued;

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? StartedAt { get; init; }

    public DateTimeOffset? CompletedAt { get; init; }

    public string? Result { get; init; }

    public string? Error { get; init; }
}

/// <summary>Run 状態機械で許可される遷移を一元化する。</summary>
public static class RunStatusMachine
{
    public static bool CanTransition(RunStatus from, RunStatus to)
    {
        return from switch
        {
            RunStatus.Queued => to is RunStatus.Running or RunStatus.Aborted,
            RunStatus.Running => to is RunStatus.AwaitingApproval or RunStatus.Succeeded or RunStatus.Failed or RunStatus.Aborted,
            RunStatus.AwaitingApproval => to is RunStatus.Running or RunStatus.Aborted,
            RunStatus.Succeeded or RunStatus.Failed or RunStatus.Aborted => false,
            _ => false,
        };
    }
}

/// <summary>承認要求の状態(5.7)。</summary>
public enum ApprovalStatus
{
    Pending,
    Approved,
    Rejected,
}

/// <summary>プロファイル設定(DI で配布)。実行プロファイルと付随情報。</summary>
public sealed class ProfileOptions
{
    public Profile Profile { get; init; } = Profile.Local;

    public string WorkspaceRoot { get; init; } = @"C:\work-agents\runs";

    public string ArtifactsRoot { get; init; } = @"C:\work-agents\artifacts";
}