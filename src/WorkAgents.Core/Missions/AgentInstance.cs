namespace WorkAgents.Core.Missions;

/// <summary>ミッション内でのエージェントインスタンスの役割。</summary>
public enum AgentInstanceRole
{
    Orchestrator,
    Member,
}

/// <summary>エージェントインスタンスの状態 (FR-010)。</summary>
public enum AgentInstanceState
{
    Idle,
    Thinking,
    ToolRunning,
    AwaitingApproval,
    AwaitingReply,
    Completed,
    Failed,
    Stopped,
}

/// <summary>ミッション内で実際に稼働しているエージェントの 1 インスタンス (data-model.md AgentInstance)。</summary>
public sealed record AgentInstance
{
    public required string InstanceId { get; init; }

    public required string MissionId { get; init; }

    public required string AgentName { get; init; }

    public required AgentInstanceRole Role { get; init; }

    public required int InstanceNo { get; init; }

    public AgentInstanceState State { get; init; } = AgentInstanceState.Idle;

    /// <summary><see cref="AgentInstanceState.AwaitingReply"/> のとき、応答を待っている相手。</summary>
    public string? AwaitingInstanceId { get; init; }

    public DateTimeOffset JoinedAt { get; init; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? LeftAt { get; init; }

    public string? JoinReason { get; init; }

    public string? LeaveReason { get; init; }

    public string? ModelName { get; init; }
}
