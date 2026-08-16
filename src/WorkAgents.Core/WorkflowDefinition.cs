namespace WorkAgents.Core;

/// <summary>1ワークフロー=1フォルダ規約で読み込まれたワークフロー定義(5.13.1)。</summary>
public sealed record WorkflowDefinition
{
    public required string Name { get; init; }
    public string DisplayName { get; init; } = "";
    public string Description { get; init; } = "";
    public string FolderPath { get; init; } = "";

    public IReadOnlyList<WorkflowStep> Steps { get; init; } = Array.Empty<WorkflowStep>();

    /// <summary>
    /// スケジュール実行用の Cron 書式(5.13.2)。null なら手動実行のみ。
    /// Local プロファイルではローカル時刻で解釈。Cronos で解析。
    /// </summary>
    public string? ScheduleCron { get; init; }

    /// <summary>この定義を採用した定義ソースの <c>Label</c>(data-model.md「解決済み定義」)。</summary>
    public string SourceLabel { get; init; } = "standard";

    /// <summary>同名で存在したが上書きされた側の <c>Label</c>(0件の場合は衝突なし)。</summary>
    public IReadOnlyList<string> OverriddenSourceLabels { get; init; } = Array.Empty<string>();
}