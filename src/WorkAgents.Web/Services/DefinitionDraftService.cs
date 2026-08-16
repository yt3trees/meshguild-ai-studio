using System.Text;
using Microsoft.Agents.AI;
using Microsoft.Extensions.Logging;
using WorkAgents.Agents;
using WorkAgents.Agents.Loading;
using WorkAgents.Core.Abstractions;
using WorkAgents.Core.Authoring;
using WorkAgents.Core.Graphs;
using WorkAgents.Core.Teams;

namespace WorkAgents.Web.Services;

/// <summary>草案生成の結果。<see cref="Definition"/> が null のときは <see cref="Error"/> に理由が入る。</summary>
public sealed record DraftResult
{
    public TeamDefinition? Team { get; init; }

    public GraphDefinition? Graph { get; init; }

    /// <summary>生成された YAML。人が読んで確かめるために常に返す。</summary>
    public string? Yaml { get; init; }

    /// <summary>草案に残っている問題。空でなくても草案自体は返す (直す前提で編集画面へ渡す)。</summary>
    public IReadOnlyList<AuthoringDiagnostic> Diagnostics { get; init; } = Array.Empty<AuthoringDiagnostic>();

    /// <summary>生成そのものに失敗した理由。</summary>
    public string? Error { get; init; }

    public bool HasDefinition => Team is not null || Graph is not null;
}

/// <summary>
/// 日本語の要望から team.yaml / graph.yaml の草案を作る (案F)。
/// 生成した YAML は必ず既存のローダーで読み直し、スキーマ違反や参照切れをその場で検出する。
/// 生成物をそのまま保存させず、必ず編集画面と要約 (案C) を経由させることを前提にしている。
/// </summary>
public sealed class DefinitionDraftService
{
    private const string DrafterName = "definition-drafter";

    private readonly DefinitionAuthoringService _authoring;
    private readonly LlmAgentFactory _factory;
    private readonly ILlmModelStore _modelStore;
    private readonly FileBasedTeamLoader _teamLoader;
    private readonly FileBasedGraphLoader _graphLoader;
    private readonly ILogger<DefinitionDraftService>? _logger;

    public DefinitionDraftService(
        DefinitionAuthoringService authoring,
        LlmAgentFactory factory,
        ILlmModelStore modelStore,
        FileBasedTeamLoader teamLoader,
        FileBasedGraphLoader graphLoader,
        ILogger<DefinitionDraftService>? logger = null)
    {
        _authoring = authoring;
        _factory = factory;
        _modelStore = modelStore;
        _teamLoader = teamLoader;
        _graphLoader = graphLoader;
        _logger = logger;
    }

    /// <summary>モデルが 1 つも設定されていない環境では草案生成を出さない。</summary>
    public async Task<bool> IsAvailableAsync(CancellationToken ct = default)
        => await _modelStore.ResolveForAgentAsync(DrafterName, ct) is not null;

    public Task<DraftResult> DraftTeamAsync(string name, string request, CancellationToken ct = default)
        => DraftAsync(DefinitionKind.Team, name, request, ct);

    public Task<DraftResult> DraftGraphAsync(string name, string request, CancellationToken ct = default)
        => DraftAsync(DefinitionKind.Graph, name, request, ct);

    private async Task<DraftResult> DraftAsync(DefinitionKind kind, string name, string request, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(request);

        var model = await _modelStore.ResolveForAgentAsync(DrafterName, ct);
        if (model is null)
        {
            return new DraftResult { Error = "LLM モデルが設定されていません。Models 画面でモデルを追加してから使ってください。" };
        }

        var definition = new AgentDefinition
        {
            Name = DrafterName,
            DisplayName = "定義ドラフター",
            Description = "日本語の要望から WorkAgents の定義 YAML を書き起こす。",
            Instructions = BuildInstructions(kind, name),
        };

        string raw;
        try
        {
            var agent = _factory.Create(definition, model);
            var response = await agent.RunAsync(request, cancellationToken: ct);
            raw = response?.ToString() ?? string.Empty;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger?.LogError(ex, "definition draft failed for {Kind} '{Name}'", kind, name);
            return new DraftResult { Error = $"草案の生成に失敗しました: {ex.Message}" };
        }

        var yaml = ExtractYaml(raw);
        if (string.IsNullOrWhiteSpace(yaml))
        {
            return new DraftResult { Error = "YAML を取り出せませんでした。もう一度試すか、テンプレートから作ってください。", Yaml = raw };
        }

        return kind == DefinitionKind.Team
            ? ParseTeam(name, yaml)
            : ParseGraph(name, yaml);
    }

    private DraftResult ParseTeam(string name, string yaml)
    {
        var folder = Path.Combine(Path.GetTempPath(), "workagents-draft", Guid.NewGuid().ToString("n"), name);
        try
        {
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "team.yaml"), yaml);
            var team = _teamLoader.Load(folder, _authoring.Agents.Select(agent => agent.Name).ToArray());
            return new DraftResult { Team = team with { FolderPath = string.Empty }, Yaml = yaml };
        }
        catch (TeamValidationException ex)
        {
            // 読めなかった草案も YAML と診断を添えて返す。書き手が手で直せる方が捨てるより早い。
            return new DraftResult
            {
                Yaml = yaml,
                Diagnostics = [ValidationMessageCatalog.ForTeam(ex.Message)],
                Error = "草案を読み込めませんでした。下の YAML を直すか、もう一度生成してください。",
            };
        }
        finally
        {
            TryDelete(Path.GetDirectoryName(folder));
        }
    }

    private DraftResult ParseGraph(string name, string yaml)
    {
        var folder = Path.Combine(Path.GetTempPath(), "workagents-draft", Guid.NewGuid().ToString("n"), name);
        try
        {
            Directory.CreateDirectory(folder);
            var graph = _graphLoader.LoadText(yaml, folder);
            var diagnostics = _authoring.ValidateGraph(graph);
            return new DraftResult
            {
                Graph = graph with { FolderPath = string.Empty },
                Yaml = yaml,
                Diagnostics = diagnostics,
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new DraftResult
            {
                Yaml = yaml,
                Error = $"草案を読み込めませんでした: {ex.Message}",
            };
        }
        finally
        {
            TryDelete(Path.GetDirectoryName(folder));
        }
    }

    /// <summary>
    /// 生成器への指示。使えるエージェント名を列挙して渡すのが要点で、
    /// 存在しない名前を書かせないための最も効く制約になる。
    /// </summary>
    private string BuildInstructions(DefinitionKind kind, string name)
    {
        var builder = new StringBuilder();
        builder.AppendLine("あなたは WorkAgents の定義ファイルを書く担当です。");
        builder.AppendLine("出力は YAML だけにしてください。説明文やコードフェンス以外の前置きを付けないこと。");
        builder.AppendLine();
        builder.AppendLine($"name は必ず {name} にしてください。version は 1 です。");
        builder.AppendLine();

        var agents = _authoring.Agents;
        builder.AppendLine("使えるエージェント名は次のものだけです。これ以外の名前を書いてはいけません。");
        if (agents.Count == 0)
        {
            builder.AppendLine("  (エージェントが 1 つも登録されていません)");
        }
        foreach (var agent in agents)
        {
            var description = string.IsNullOrWhiteSpace(agent.Description) ? "説明なし" : agent.Description;
            builder.AppendLine($"  - {agent.Name}: {description}");
        }
        builder.AppendLine();

        if (kind == DefinitionKind.Graph)
        {
            var teams = _authoring.Teams;
            builder.AppendLine("使えるチーム名は次のものだけです。");
            if (teams.Count == 0)
            {
                builder.AppendLine("  (チームが 1 つも登録されていません。kind: team のノードは使わないでください)");
            }
            foreach (var team in teams)
            {
                builder.AppendLine($"  - {team.Name}");
            }
            builder.AppendLine();
        }

        builder.AppendLine("守る規則:");
        builder.AppendLine(kind == DefinitionKind.Team
            ? TeamRules
            : GraphRules);

        return builder.ToString();
    }

    private const string TeamRules = """
          - トップレベルのキーは version, name, displayName, description, orchestrator, members, channels, limits, evaluation だけ。
          - orchestrator.agent は必須。members は 1 件以上必要で、各要素は agent, role, scope, maxInstances を持てる。
          - 同じエージェントを members に 2 回書かない。増やしたいときは maxInstances を上げる。
          - channels.default は via-orchestrator か direct。channels.allow[].kinds は question / answer / share のみ。
          - channels.allow の from と to は、そのチームの orchestrator か members に含まれる名前だけ。
          - limits.maxDelegationDepth は 1 以上 10 以下。orchestrator と members の maxInstances の合計は limits.maxParallelInstances 以下にする。
        """;

    private const string GraphRules = """
          - トップレベルのキーは version, name, displayName, description, defaults, nodes, edges, subgraphs, layout だけ。
          - nodes[].kind は agent / team / code / approval / branch / parallel / join / loop / subgraph のいずれか。
          - kind: agent は agent と input、kind: team は team と goal、kind: code は codeFile、kind: approval は title と summary を書く。
          - kind: join は joinPolicy (all または any) が必須。kind: loop は stop が必須で、maxIterations などを 1 つ以上指定する。
          - edges は from と to を持つ。分岐させるときは condition を書くが、条件なしの既定エッジを必ず 1 本残す。
          - 後戻りするエッジには loopBack: true を付ける。付けない循環は不正になる。
          - 到達できないノードを作らない。すべてのノードが開始ノードから辿れるようにする。
          - 他ノードの出力は ${nodes.<ノード ID>.output}、ミッションの目標は ${mission.goal} で参照する。
        """;

    /// <summary>コードフェンスや前置きが混ざっていても YAML 本体を取り出す。</summary>
    internal static string ExtractYaml(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        var fenceStart = raw.IndexOf("```", StringComparison.Ordinal);
        if (fenceStart >= 0)
        {
            var afterFence = raw.IndexOf('\n', fenceStart);
            if (afterFence > 0)
            {
                var fenceEnd = raw.IndexOf("```", afterFence, StringComparison.Ordinal);
                if (fenceEnd > afterFence)
                {
                    return raw[(afterFence + 1)..fenceEnd].Trim();
                }
            }
        }

        return raw.Trim();
    }

    private void TryDelete(string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return;
        }
        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger?.LogDebug(ex, "failed to clean up draft temp directory {Path}", path);
        }
    }
}
