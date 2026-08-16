namespace WorkAgents.Core.Missions;

/// <summary>要約が覆う境界種別。</summary>
public enum SummaryBoundaryKind
{
    Iteration,
    Node,
}

/// <summary>会話の要約 (data-model.md Conversation / conversation_summaries)。</summary>
public sealed record ConversationSummary
{
    public required string SummaryId { get; init; }

    public required string MissionId { get; init; }

    public string ThreadKey { get; init; } = "main";

    public required long CoversUpToSeq { get; init; }

    public required string Body { get; init; }

    public required SummaryBoundaryKind BoundaryKind { get; init; }

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}
