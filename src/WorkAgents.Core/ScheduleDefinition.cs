namespace WorkAgents.Core;

/// <summary>スケジュール定義(5.13.2)。name は一意(workflow 名と等しい運用を想定)。</summary>
public sealed record ScheduleDefinition
{
    public required string Name { get; init; }
    public required string WorkflowName { get; init; }

    /// <summary>実行時に workflow.input へ渡す入力テキスト。</summary>
    public string Input { get; init; } = "";

    /// <summary>Cron 書式(5 段階 or 6 段階)。Cronos で解析する。</summary>
    public string? Cron { get; init; }

    public bool Enabled { get; init; } = true;

    public DateTimeOffset? LastRunAt { get; init; }

    /// <summary>次回実行予定時刻。null なら未計算(実行対象外)。</summary>
    public DateTimeOffset? NextRunAt { get; init; }

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; init; } = DateTimeOffset.UtcNow;
}