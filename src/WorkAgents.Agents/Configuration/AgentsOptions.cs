namespace WorkAgents.Agents.Configuration;

/// <summary>
/// 定義ソース1件(contracts/definition-source-config.md)。<see cref="Path"/> 配下に
/// <c>agents/</c>, <c>skills/</c>, <c>teams/</c>, <c>graphs/</c>, <c>workflows/</c> の
/// サブディレクトリを内包する。
/// </summary>
public sealed class DefinitionSourceEntry
{
    public string Label { get; init; } = string.Empty;

    public string Path { get; init; } = string.Empty;
}

/// <summary>チーム固有ツールプラグインの到達先ホスト許可設定(FR-009)。</summary>
public sealed class ToolPluginsOptions
{
    /// <summary>
    /// チーム固有ツールが到達してよいホストのallowlist。空の場合は制限なし(既存挙動と後方互換)。
    /// </summary>
    public List<string> AllowedHosts { get; init; } = [];
}

/// <summary>
/// <c>Agents</c> 設定セクション(contracts/definition-source-config.md)。
/// 未設定時は既存の単一固定パス読み込みにフォールバックする(後方互換)。
/// </summary>
public sealed class AgentsOptions
{
    public const string SectionName = "Agents";

    /// <summary>
    /// 読み込む定義ソースの順序付きリスト。先頭が共通システム標準、以降がチーム定義パッケージ。
    /// 後勝ちで同名定義をマージする。空の場合は既存互換の単一標準パスにフォールバックする。
    /// </summary>
    public List<DefinitionSourceEntry> DefinitionSources { get; init; } = [];

    /// <summary>チーム固有ツールのアセンブリ(DLL)を配置するディレクトリのリスト。</summary>
    public List<string> ToolPluginDirectories { get; init; } = [];

    public ToolPluginsOptions ToolPlugins { get; init; } = new();
}
