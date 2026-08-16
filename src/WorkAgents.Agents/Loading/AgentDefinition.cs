namespace WorkAgents.Agents.Loading;

/// <summary>
/// 1エージェント=1フォルダ規約(5.2)で解決済みのエージェント定義。
/// <list type="bullet">
/// <item><see cref="Name"/>: agent.yaml の <c>name</c>(未設定時はフォルダ名)。</item>
/// <item><see cref="Instructions"/>: <c>instructions.md</c> の中身。薄く保つ・詳細は SKILL.md へ。</item>
/// </list>
/// </summary>
public sealed class AgentDefinition
{
    public required string Name { get; init; }

    /// <summary>agent.yaml の <c>kind</c>(参考用の種別)。GUI で書き戻すときに落とさないため保持する。</summary>
    public string? Kind { get; init; }

    public string DisplayName { get; init; } = "";
    public string Description { get; init; } = "";
    public string Instructions { get; init; } = "";
    public string FolderPath { get; init; } = "";
    public IReadOnlyList<string> SharedSkillNames { get; init; } = Array.Empty<string>();
    /// <summary>共有スキル名から、複数定義ソースをマージした実体フォルダーへの解決結果。</summary>
    public IReadOnlyDictionary<string, string> SharedSkillPaths { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyList<string> LocalSkillNames { get; init; } = Array.Empty<string>();

    /// <summary>この定義を採用した定義ソースの <c>Label</c>(data-model.md「解決済み定義」)。</summary>
    public string SourceLabel { get; init; } = "standard";

    /// <summary>同名で存在したが上書きされた側の <c>Label</c>(0件の場合は衝突なし)。</summary>
    public IReadOnlyList<string> OverriddenSourceLabels { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Harness のシェル付与フラ(5.4)。true なら ShellExecutor を付与した HarnessAgent を構築する。
    /// <c>agent.yaml</c> の <c>harness.shell</c>。未設定時は false(成果物専用エージェント)。
    /// </summary>
    public bool HarnessShell { get; init; }

    /// <summary>
    /// FileStore 種別(<c>"workspace"</c>/<c>"artifacts"</c>/null)。5.12 の使い分け参照。
    /// <c>"workspace"</c>: run ごとの作業FSを FileMemoryStore+FileAccessStore に割り当て(シェル作業向け)。
    /// <c>"artifacts"</c>/null: 成果物ドロップのみ。FileStore は付与しない(シェルなしと組合せ)。
    /// </summary>
    public string? HarnessFileStore { get; init; }

}
