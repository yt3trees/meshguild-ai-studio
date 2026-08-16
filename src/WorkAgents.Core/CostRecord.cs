namespace WorkAgents.Core;

/// <summary>
/// 1回のAgent実行(Host非同期RunまたはWeb同期チャットの1メッセージ・ワークフロー1ステップ)の
/// トークン使用量記録(5.10, 第6章 `costs` テーブル)。
/// 予算判定・上限到達時のAbort・コストダッシュボードはM6の後続範囲で、本レコードはその土台となる
/// 使用量集計のみを担う。
/// </summary>
public sealed record CostRecord
{
    /// <summary>Host非同期Runのrun_id。Web同期チャット等runIdが無い呼び出しではnull。</summary>
    public string? RunId { get; init; }

    /// <summary>会話スレッドID。無ければnull。</summary>
    public string? ThreadId { get; init; }

    public required string AgentName { get; init; }
    public string? ModelName { get; init; }
    public string? Provider { get; init; }
    public long? InputTokens { get; init; }
    public long? OutputTokens { get; init; }
    public long? TotalTokens { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>このコスト記録自体の識別子 (data-model.md CostRecord、T032)。Message から辿るための参照キー。</summary>
    public string? CostRecordId { get; init; }

    /// <summary>所属ミッション。</summary>
    public string? MissionId { get; init; }

    /// <summary>どのエージェントインスタンスの消費か。</summary>
    public string? AgentInstanceId { get; init; }

    /// <summary>どのノード実行の消費か。</summary>
    public string? NodeRunId { get; init; }

    /// <summary>どの反復の消費か。</summary>
    public string? IterationId { get; init; }

    /// <summary>推定コスト (USD)。</summary>
    public double? EstimatedCostUsd { get; init; }
}
