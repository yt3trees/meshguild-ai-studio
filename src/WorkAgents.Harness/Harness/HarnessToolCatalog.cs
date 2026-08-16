namespace WorkAgents.Harness.Harness;

/// <summary>Harness がエージェントへ公開する組み込みツールの表示用メタデータ。</summary>
public sealed record HarnessToolDescriptor(
    string Name,
    string Description,
    string Source,
    string Approval);

/// <summary>
/// Harness の設定に応じて公開される組み込みツールのマニフェスト。
/// <c>web_search</c> は <see cref="HarnessAgentFactory"/> が <c>DisableWebSearch = true</c> で
/// 無効化しているため一覧に含めない(egress未制御・OpenAI Responses APIの組込みweb_searchは
/// 対応モデル/デプロイが限定的で非対応先では`web_search_options`がunknown_parameterエラーになるため)。
/// </summary>
public static class HarnessToolCatalog
{
    private const string HostingSource = "Microsoft.Agents.AI.Hosting";
    private const string ShellSource = "Microsoft.Agents.AI.Tools.Shell";

    private static readonly IReadOnlyList<HarnessToolDescriptor> BaseTools =
    [
        Tool("file_access_delete", "run workspace 内のファイルを削除"),
        Tool("file_access_grep", "run workspace 内のファイル内容を検索"),
        Tool("file_access_ls", "run workspace 内のファイル一覧を取得"),
        Tool("file_access_read", "run workspace 内のファイルを読み取り"),
        Tool("file_access_replace", "run workspace 内の文字列を置換"),
        Tool("file_access_replace_lines", "run workspace 内の行範囲を置換"),
        Tool("file_access_write", "run workspace 内のファイルへ書き込み"),
        Tool("file_memory_delete", "ファイルメモリを削除"),
        Tool("file_memory_grep", "ファイルメモリを検索"),
        Tool("file_memory_ls", "ファイルメモリの一覧を取得"),
        Tool("file_memory_read", "ファイルメモリを読み取り"),
        Tool("file_memory_replace", "ファイルメモリの文字列を置換"),
        Tool("file_memory_replace_lines", "ファイルメモリの行範囲を置換"),
        Tool("file_memory_write", "ファイルメモリへ書き込み"),
        Tool("mode_get", "現在の Harness モードを取得"),
        Tool("mode_set", "Harness モードを変更"),
        Tool("todos_add", "作業項目を追加"),
        Tool("todos_complete", "作業項目を完了"),
        Tool("todos_get_all", "すべての作業項目を取得"),
        Tool("todos_get_remaining", "未完了の作業項目を取得"),
        Tool("todos_remove", "作業項目を削除"),
    ];

    private static readonly HarnessToolDescriptor ShellTool = new(
        "run_shell",
        "拘束された run workspace 内でコマンドを実行",
        ShellSource,
        "required");

    private static readonly IReadOnlyList<HarnessToolDescriptor> SkillTools =
    [
        Tool("load_skill", "アタッチされた skill の指示を読み込み"),
        Tool("read_skill_resource", "skill に含まれるリソースを読み取り"),
        Tool("run_skill_script", "skill に含まれるスクリプトを実行"),
    ];

    public static IReadOnlySet<string> AllNames { get; } = BaseTools
        .Append(ShellTool)
        .Concat(SkillTools)
        .Select(tool => tool.Name)
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<HarnessToolDescriptor> List(bool shellEnabled, bool skillsEnabled)
    {
        var tools = new List<HarnessToolDescriptor>(BaseTools.Count + 4);
        tools.AddRange(BaseTools);
        if (shellEnabled)
        {
            tools.Add(ShellTool);
        }
        if (skillsEnabled)
        {
            tools.AddRange(SkillTools);
        }
        return tools;
    }

    private static HarnessToolDescriptor Tool(string name, string description)
        => new(name, description, HostingSource, "automatic");
}