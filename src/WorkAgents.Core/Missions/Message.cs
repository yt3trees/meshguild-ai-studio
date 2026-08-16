namespace WorkAgents.Core.Missions;

/// <summary>発言送信者の種別。</summary>
public enum MessageSenderKind
{
    Agent,
    Human,
    System,
}

/// <summary>発言種別 (FR-007)。</summary>
public enum MessageKind
{
    Delegate,
    Question,
    Answer,
    Share,
    Handoff,
    Report,
    HumanInstruction,
    SystemNote,
    RosterChange,
    Rejected,
}

/// <summary>会話上の 1 発言。記録であると同時に制御経路そのものである (data-model.md Message)。</summary>
public sealed record Message
{
    public required string MessageId { get; init; }

    public required string MissionId { get; init; }

    public required long Seq { get; init; }

    public string ThreadKey { get; init; } = "main";

    public required MessageSenderKind SenderKind { get; init; }

    public string? SenderInstanceId { get; init; }

    public string? RecipientInstanceId { get; init; }

    public required MessageKind Kind { get; init; }

    public required string Body { get; init; }

    public string? InReplyTo { get; init; }

    public int DelegationDepth { get; init; }

    public string? NodeRunId { get; init; }

    public string? IterationId { get; init; }

    /// <summary>参照した入力の JSON 配列 (成果物 ID、要約 ID、指示 ID) — FR-009。</summary>
    public string? InputRefs { get; init; }

    public string? CostRecordId { get; init; }

    public DateTimeOffset? DiscardedAt { get; init; }

    public string? DiscardedByCheckpointId { get; init; }

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}
