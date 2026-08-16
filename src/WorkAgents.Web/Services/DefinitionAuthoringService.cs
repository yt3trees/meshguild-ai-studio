using Microsoft.Extensions.Logging;
using WorkAgents.Agents.Configuration;
using WorkAgents.Agents.Loading;
using WorkAgents.Core.Authoring;
using WorkAgents.Core.Graphs;
using WorkAgents.Core.Teams;
using WorkAgents.Orchestration.Graph;

namespace WorkAgents.Web.Services;

/// <summary>保存の結果。<see cref="Diagnostics"/> が空でないときは書き込みを行っていない。</summary>
public sealed record SaveResult(bool Saved, string? Path, IReadOnlyList<AuthoringDiagnostic> Diagnostics)
{
    public static SaveResult Rejected(IReadOnlyList<AuthoringDiagnostic> diagnostics) => new(false, null, diagnostics);

    public static SaveResult Ok(string path) => new(true, path, Array.Empty<AuthoringDiagnostic>());
}

/// <summary>
/// GUI からの定義の読み書きをまとめる (案A〜E の受け皿)。
/// <list type="bullet">
/// <item>編集用のスナップショットを保持し、保存後にディスクから読み直す。</item>
/// <item>保存前に必ず検証し、失敗したら書き込まない。</item>
/// <item>検証結果は <see cref="AuthoringDiagnostic"/> (日本語) に変換して返す。</item>
/// </list>
/// 実行エンジン側の定義は起動時に DI へ焼き込まれているため、保存内容が実行に反映されるのは
/// アプリの再起動後になる。GUI 側でその旨を表示すること。
/// </summary>
public sealed class DefinitionAuthoringService
{
    private readonly AgentsOptions _options;
    private readonly FileBasedAgentLoader _agentLoader;
    private readonly FileBasedTeamLoader _teamLoader;
    private readonly FileBasedGraphLoader _graphLoader;
    private readonly TeamYamlWriter _teamWriter;
    private readonly GraphYamlWriter _graphWriter;
    private readonly AgentYamlWriter _agentWriter;
    private readonly ILogger<DefinitionAuthoringService>? _logger;
    private readonly Lock _gate = new();

    /// <summary>フォルダー名として使える範囲。GUI の新規作成でも同じ規則で弾く。</summary>
    private static readonly System.Text.RegularExpressions.Regex AgentNamePattern =
        new("^[a-z0-9][a-z0-9_-]*$", System.Text.RegularExpressions.RegexOptions.Compiled);

    private static readonly string[] AllowedFileStores = ["workspace", "artifacts"];

    private IReadOnlyList<AgentDefinition> _agents = Array.Empty<AgentDefinition>();
    private IReadOnlyList<TeamDefinition> _teams = Array.Empty<TeamDefinition>();
    private IReadOnlyList<GraphDefinition> _graphs = Array.Empty<GraphDefinition>();
    private IReadOnlyList<SharedSkillInfo> _skills = Array.Empty<SharedSkillInfo>();

    public DefinitionAuthoringService(
        AgentsOptions options,
        FileBasedAgentLoader agentLoader,
        FileBasedTeamLoader teamLoader,
        FileBasedGraphLoader graphLoader,
        TeamYamlWriter teamWriter,
        GraphYamlWriter graphWriter,
        AgentYamlWriter agentWriter,
        ILogger<DefinitionAuthoringService>? logger = null)
    {
        _options = options;
        _agentLoader = agentLoader;
        _teamLoader = teamLoader;
        _graphLoader = graphLoader;
        _teamWriter = teamWriter;
        _graphWriter = graphWriter;
        _agentWriter = agentWriter;
        _logger = logger;
        Reload();
    }

    public IReadOnlyList<AgentDefinition> Agents { get { lock (_gate) { return _agents; } } }

    public IReadOnlyList<TeamDefinition> Teams { get { lock (_gate) { return _teams; } } }

    public IReadOnlyList<GraphDefinition> Graphs { get { lock (_gate) { return _graphs; } } }

    /// <summary>ディスクに置かれている共有スキル。参照されていないものも含む。</summary>
    public IReadOnlyList<SharedSkillInfo> SharedSkills { get { lock (_gate) { return _skills; } } }

    /// <summary>保存や外部編集のあとにディスクから読み直す。</summary>
    public void Reload()
    {
        var agents = _agentLoader.LoadFromSources(_options.DefinitionSources);
        var agentNames = agents.Select(agent => agent.Name).ToArray();
        var teams = _teamLoader.LoadAllFromSources(_options.DefinitionSources, agentNames);
        var graphs = _graphLoader.LoadAllFromSources(
            _options.DefinitionSources,
            agentNames,
            teams.Select(team => team.Name).ToArray());
        var skills = SharedSkillCatalog.ListFromSources(_options.DefinitionSources);

        lock (_gate)
        {
            _agents = agents;
            _teams = teams;
            _graphs = graphs;
            _skills = skills;
        }
    }

    public AgentDefinition? FindAgent(string name)
        => Agents.FirstOrDefault(agent => string.Equals(agent.Name, name, StringComparison.OrdinalIgnoreCase));

    public TeamDefinition? FindTeam(string name)
        => Teams.FirstOrDefault(team => string.Equals(team.Name, name, StringComparison.OrdinalIgnoreCase));

    public GraphDefinition? FindGraph(string name)
        => Graphs.FirstOrDefault(graph => string.Equals(graph.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// 新規定義の書き込み先。定義ソースは後勝ちなので、最後のソースに置けば
    /// 標準定義を上書きする形にも、独自定義を足す形にもなる。
    /// </summary>
    public string WritableRoot => _options.DefinitionSources.Count > 0
        ? _options.DefinitionSources[^1].Path
        : AppContext.BaseDirectory;

    public string WritableSourceLabel => _options.DefinitionSources.Count > 0
        ? _options.DefinitionSources[^1].Label
        : "standard";

    // --------------------------------------------------------------- 参照候補

    /// <summary>x-source を解決するための選択肢一式を組み立てる (案A)。</summary>
    public ReferenceOptions BuildReferenceOptions(GraphDefinition? graph = null, TeamDefinition? team = null)
        => new()
        {
            // ラベルは表示名ではなく name にする。YAML に書き込まれるのはこちらなので、
            // 表示名だけ見せると「選んだもの」と「書かれるもの」が食い違って見える。
            Agents = Agents
                .Select(agent => new ReferenceOption(agent.Name, agent.Name, Describe(agent.DisplayName, agent.Description)))
                .OrderBy(option => option.Value, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            Teams = Teams
                .Select(item => new ReferenceOption(item.Name, item.Name, Describe(item.DisplayName, item.Description)))
                .OrderBy(option => option.Value, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            Graphs = Graphs
                .Select(item => new ReferenceOption(item.Name, item.Name, Describe(item.DisplayName, item.Description)))
                .OrderBy(option => option.Value, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            // 候補はディスク上の SKILL.md から作る。参照済みの名前だけを集めると、
            // 置いたばかりでまだどこからも使われていないスキルが選べなくなる。
            Skills = SharedSkills
                .Select(skill => new ReferenceOption(skill.Name, skill.Name, Trim(skill.Description)))
                .OrderBy(option => option.Value, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            Graph = graph,
            Team = team,
        };

    // ----------------------------------------------------------------- graph

    /// <summary>グラフを検証して日本語の診断を返す。保存はしない。</summary>
    public IReadOnlyList<AuthoringDiagnostic> ValidateGraph(GraphDefinition graph)
    {
        ArgumentNullException.ThrowIfNull(graph);
        var validator = new GraphValidator(
            Agents.Select(agent => agent.Name).ToArray(),
            Teams.Select(team => team.Name).ToArray(),
            Graphs.Select(item => item.Name).ToArray());
        return validator.Validate(graph).ToDiagnostics();
    }

    public string ToYaml(GraphDefinition graph) => _graphWriter.ToYaml(graph);

    public string ToYaml(TeamDefinition team) => _teamWriter.ToYaml(team);

    /// <summary>検証を通ったグラフだけを graph.yaml へ書き出す。</summary>
    public async Task<SaveResult> SaveGraphAsync(GraphDefinition graph, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(graph);
        var folder = string.IsNullOrWhiteSpace(graph.FolderPath)
            ? Path.Combine(WritableRoot, "graphs", graph.Name)
            : graph.FolderPath;

        // name とフォルダー名の一致は検証対象なので、書き出し先を先に決めてから検証する。
        var target = graph with { FolderPath = folder };
        var diagnostics = ValidateGraph(target);
        if (diagnostics.Count > 0)
        {
            return SaveResult.Rejected(diagnostics);
        }

        var path = Path.Combine(folder, "graph.yaml");
        await _graphWriter.WriteAsync(target, path, ct);
        _logger?.LogInformation("saved graph '{Name}' to {Path}", graph.Name, path);
        Reload();
        return SaveResult.Ok(path);
    }

    // ------------------------------------------------------------------ team

    /// <summary>
    /// チームを検証して日本語の診断を返す。
    /// ローダーが唯一の検証実装なので、書き出した YAML を読み直して確かめる
    /// (GUI 用に検証規則を書き写すと、必ずローダー側とずれるため)。
    /// </summary>
    public IReadOnlyList<AuthoringDiagnostic> ValidateTeam(TeamDefinition team)
    {
        ArgumentNullException.ThrowIfNull(team);
        var diagnostics = new List<AuthoringDiagnostic>();

        var temporary = Path.Combine(Path.GetTempPath(), "workagents-authoring", Guid.NewGuid().ToString("n"), team.Name);
        try
        {
            Directory.CreateDirectory(temporary);
            File.WriteAllText(Path.Combine(temporary, "team.yaml"), _teamWriter.ToYaml(team));
            _teamLoader.Load(temporary, Agents.Select(agent => agent.Name).ToArray());
        }
        catch (TeamValidationException ex)
        {
            diagnostics.Add(ValidationMessageCatalog.ForTeam(ex.Message));
        }
        finally
        {
            TryDeleteDirectory(Path.GetDirectoryName(temporary));
        }

        return diagnostics;
    }

    /// <summary>検証を通ったチームだけを team.yaml へ書き出す。</summary>
    public async Task<SaveResult> SaveTeamAsync(TeamDefinition team, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(team);
        var diagnostics = ValidateTeam(team);
        if (diagnostics.Count > 0)
        {
            return SaveResult.Rejected(diagnostics);
        }

        var folder = string.IsNullOrWhiteSpace(team.FolderPath)
            ? Path.Combine(WritableRoot, "teams", team.Name)
            : team.FolderPath;
        var path = Path.Combine(folder, "team.yaml");
        await _teamWriter.WriteAsync(team, path, ct);
        _logger?.LogInformation("saved team '{Name}' to {Path}", team.Name, path);
        Reload();
        return SaveResult.Ok(path);
    }

    // ----------------------------------------------------------------- agent

    /// <summary>
    /// エージェントを検証して日本語の診断を返す。
    /// エージェントのローダーは不正な値を捨ててログに落とすだけなので、team のように
    /// 「書き出して読み直す」方式では検証できない。ここが唯一の検証実装になる。
    /// </summary>
    public IReadOnlyList<AuthoringDiagnostic> ValidateAgent(AgentDefinition agent)
    {
        ArgumentNullException.ThrowIfNull(agent);
        var diagnostics = new List<AuthoringDiagnostic>();

        if (string.IsNullOrWhiteSpace(agent.Name))
        {
            diagnostics.Add(ValidationMessageCatalog.ForAgent("agent name is required"));
        }
        else if (!AgentNamePattern.IsMatch(agent.Name))
        {
            diagnostics.Add(ValidationMessageCatalog.ForAgent("invalid agent name"));
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var skill in agent.SharedSkillNames)
        {
            if (!seen.Add(skill))
            {
                diagnostics.Add(ValidationMessageCatalog.ForAgent($"duplicate skill: {skill}"));
                continue;
            }
            if (!SharedSkills.Any(known => string.Equals(known.Name, skill, StringComparison.OrdinalIgnoreCase)))
            {
                diagnostics.Add(ValidationMessageCatalog.ForAgent($"unknown skill: {skill}"));
            }
        }

        if (!string.IsNullOrWhiteSpace(agent.HarnessFileStore)
            && !AllowedFileStores.Contains(agent.HarnessFileStore, StringComparer.Ordinal))
        {
            diagnostics.Add(ValidationMessageCatalog.ForAgent("unknown fileStore"));
        }

        return diagnostics;
    }

    public string ToYaml(AgentDefinition agent) => _agentWriter.ToYaml(agent);

    /// <summary>検証を通ったエージェントだけを agent.yaml と instructions.md へ書き出す。</summary>
    public async Task<SaveResult> SaveAgentAsync(AgentDefinition agent, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(agent);
        var diagnostics = ValidateAgent(agent);
        if (diagnostics.Count > 0)
        {
            return SaveResult.Rejected(diagnostics);
        }

        var folder = string.IsNullOrWhiteSpace(agent.FolderPath)
            ? Path.Combine(WritableRoot, "agents", agent.Name)
            : agent.FolderPath;
        await _agentWriter.WriteAsync(agent, folder, ct);
        var path = Path.Combine(folder, "agent.yaml");
        _logger?.LogInformation("saved agent '{Name}' to {Path}", agent.Name, path);
        Reload();
        return SaveResult.Ok(path);
    }

    /// <summary>同名の定義が既にあるか。新規作成時の上書き防止に使う。</summary>
    public bool GraphExists(string name) => FindGraph(name) is not null;

    public bool TeamExists(string name) => FindTeam(name) is not null;

    public bool AgentExists(string name) => FindAgent(name) is not null;

    private void TryDeleteDirectory(string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return;
        }
        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch (IOException ex)
        {
            _logger?.LogDebug(ex, "failed to clean up authoring temp directory {Path}", path);
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger?.LogDebug(ex, "failed to clean up authoring temp directory {Path}", path);
        }
    }

    /// <summary>選択肢の補足。表示名と説明のうち、あるものを短くまとめる。</summary>
    private static string? Describe(string? displayName, string? description)
    {
        var parts = new[] { displayName, description }
            .Where(part => !string.IsNullOrWhiteSpace(part))
            .Select(part => part!.Trim());
        return Trim(string.Join(" / ", parts));
    }

    private static string? Trim(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }
        var single = text.ReplaceLineEndings(" ").Trim();
        return single.Length <= 60 ? single : single[..60] + "…";
    }
}
