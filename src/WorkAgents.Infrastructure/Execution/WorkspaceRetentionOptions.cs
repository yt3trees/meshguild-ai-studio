namespace WorkAgents.Infrastructure.Execution;

/// <summary>Run単位のワークスペースディレクトリの保持期限スイープ設定(appsettingsの`Workspace:Retention`)。</summary>
public sealed class WorkspaceRetentionOptions
{
    /// <summary>保持期限スイープ自体の有効/無効。既定 true。</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>完了したRunのワークスペースを保持する期間。既定7日間。</summary>
    public TimeSpan RetentionPeriod { get; init; } = TimeSpan.FromDays(7);

    /// <summary>スイープのポーリング間隔。既定1時間。</summary>
    public TimeSpan SweepInterval { get; init; } = TimeSpan.FromHours(1);
}
