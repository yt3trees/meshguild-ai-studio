using System.Diagnostics;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Compaction;
using Microsoft.Agents.AI.Tools.Shell;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using WorkAgents.Core;
using WorkAgents.Harness.GitAuth;

namespace WorkAgents.Harness.Harness;

/// <summary>
/// Harness エージェント構築の入力(WorkAgents.Agents 側から AgentDefinition を介さず渡すための
/// 依存方向を維持するための軽量設定)。AgentDefinition(WorkAgents.Agents) に依存しない。
/// </summary>
public sealed class HarnessAgentConfig
{
    public required string Name { get; init; }

    public required string Instructions { get; init; }

    /// <summary>エージェントフォルダ絶対パス(<c>workspace.yaml</c> をここから読む)。未設定時は workspace.yaml 無し。</summary>
    public string? AgentFolderPath { get; init; }

    /// <summary>"workspace" / "artifacts" / null(5.12)。null か "artifacts" で FileStore/shell 付与なし。</summary>
    public string? FileStoreKind { get; init; }

    /// <summary><c>true</c> のとき、作業ディレクトリに拘束した ShellExecutor を付与する。</summary>
    public bool ShellEnabled { get; init; }

    /// <summary>このエージェントが読み込める skill ディレクトリ。先に指定したパスの同名 skill を優先する。</summary>
    public IReadOnlyList<string> SkillPaths { get; init; } = Array.Empty<string>();

    /// <summary>Agent固有の関数ツール。Shell/FileStoreの組み込みツールとは別に追加される。</summary>
    public IReadOnlyList<AITool> CustomTools { get; init; } = Array.Empty<AITool>();

    /// <summary>
    /// 作業ディレクトリ(明示上書き)。未指定時は ProfileOptions.WorkspaceRoot/&lt;agentName&gt;/&lt;guid&gt; を新建。
    /// M3 で runId ごとのディレクトリを外から渡す経路を追加する。
    /// </summary>
    public string? WorkingDirectory { get; init; }

    public int MaxContextWindowTokens { get; init; } = 128_000;

    public int MaxOutputTokens { get; init; } = 4_096;

    public int CompactionTriggerTokens { get; init; } = 96_000;

    public int CompactionTargetTokens { get; init; } = 64_000;

    public int CompactionMinimumPreservedGroups { get; init; } = 8;
}

/// <summary>
/// FileStore + ShellExecutor(denylist+confine) を付与した <c>HarnessAgent</c>(<see cref="AIAgent"/>) を構築する(5.4, 5.12)。
/// エージェント定義の <c>harness.shell</c>/<c>harness.fileStore</c> と <c>workspace.yaml</c> を解釈する。
/// シェルを渡さなければコマンド実行ツールは生えない(成果物専用エージェントは安全)。
/// </summary>
public sealed class HarnessAgentFactory
{
    private readonly ProfileOptions _profile;
    private readonly ILogger<HarnessAgentFactory>? _logger;
    private readonly IGitAuth? _gitAuth;

    public HarnessAgentFactory(ProfileOptions profile, ILogger<HarnessAgentFactory>? logger = null, IGitAuth? gitAuth = null)
    {
        _profile = profile;
        _logger = logger;
        _gitAuth = gitAuth;
    }

    /// <summary>
    /// Harness エージェントを構築して返す。シェル不要(<c>FileStoreKind</c> が workspace でない)なら
    /// FileStore のみ、または最小(FileStore/shell ともに付与しない)の HarnessAgent を返す。
    /// </summary>
    public AIAgent Create(
        IChatClient chatClient,
        HarnessAgentConfig config,
        ILoggerFactory? loggerFactory = null)
    {
        var ws = LoadWorkspace(config);
        var workDir = ResolveWorkDir(config, ws);
        Directory.CreateDirectory(workDir);
        _logger?.LogInformation("harness workspace dir={Dir} agent={Agent}", workDir, config.Name);

        var policy = ShellPolicyFactory.Build(ws?.Shell);

        var fileStore = new FileSystemAgentFileStore(workDir);
        LocalShellExecutor? shell = null;
        if (config.ShellEnabled)
        {
            InitializeGitAuthBestEffort();
            shell = BuildShell(workDir, ws);
        }

        var skillPaths = config.SkillPaths
            .Where(Directory.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var lf = loggerFactory ?? NullLoggerFactory.Instance;
        var options = new HarnessAgentOptions
        {
            Name = config.Name,
            ChatOptions = new ChatOptions
            {
                Instructions = config.Instructions,
                Tools = config.CustomTools.ToList(),
            },
            FileMemoryStore = fileStore,
            FileAccessStore = fileStore,
            ShellExecutor = shell,
            // M4: ShellはToolApprovalRequestContentとして呼び出し側へ返し、承認後に同じSessionで再開する。
            DisableShellToolApproval = false,
            DisableToolAutoApproval = true,
            DisableAgentSkillsProvider = skillPaths.Length == 0,
            AgentSkillsSource = skillPaths.Length == 0
                ? null
                : new AgentFileSkillsSource(skillPaths, scriptRunner: null, options: null, loggerFactory: lf),
            // 無限ループ対策の最小安全弁(本格的 run タイムアウト/予算は M6)。
            MaximumIterationsPerRequest = 50,
            MaxContextWindowTokens = config.MaxContextWindowTokens,
            MaxOutputTokens = config.MaxOutputTokens,
            CompactionStrategy = CreateCompactionStrategy(chatClient, config),
            // egress 持ち出し経路を塞ぐ一助としてファイルアクセス FileMemory/FileAccess は作業FSに限定済み。
            // Web検索は既定で無効化する: (1) このリポジトリにWeb検索を意図的に使う設計・設定項目が無い、
            // (2) OpenAI Responses APIの組込みweb_searchはモデル/デプロイ限定の機能で、非対応デプロイでは
            //     `web_search_options`がunknown_parameterとして400になり会話自体が失敗する、
            // (3) egress allowlist(M7)が無い段階で不要な外部通信経路を開けないため。
            DisableWebSearch = true,
        };

        return chatClient.AsHarnessAgent(options, lf, services: null);
    }

    private static SummarizationCompactionStrategy CreateCompactionStrategy(
        IChatClient chatClient,
        HarnessAgentConfig config)
    {
        return new SummarizationCompactionStrategy(
            chatClient,
            CompactionTriggers.TokensExceed(config.CompactionTriggerTokens),
            config.CompactionMinimumPreservedGroups,
            summarizationPrompt: null,
            target: CompactionTriggers.TokensBelow(config.CompactionTargetTokens));
    }

    /// <summary>
    /// 作業ディレクトリの決定順は (1) 明示 <c>WorkingDirectory</c>(M3のrun別ディレクトリ)、
    /// (2) <c>workspace.yaml</c> の <c>fileStore.root</c>、(3) <c>ProfileOptions.WorkspaceRoot</c>。
    /// </summary>
    private string ResolveWorkDir(HarnessAgentConfig config, WorkspaceYaml? ws)
    {
        if (!string.IsNullOrWhiteSpace(config.WorkingDirectory))
        {
            return Path.GetFullPath(config.WorkingDirectory);
        }

        var configuredRoot = ws?.FileStore?.Root;
        var root = !string.IsNullOrWhiteSpace(configuredRoot)
            ? configuredRoot
            : (string.IsNullOrWhiteSpace(_profile.WorkspaceRoot) ? @"C:\work-agents\runs" : _profile.WorkspaceRoot);
        return Path.GetFullPath(Path.Combine(root, config.Name, Guid.NewGuid().ToString("N")));
    }

    /// <summary>
    /// シェルを許可するエージェントの構築前にGit認証(installation token発行+git-credentials書き込み)を
    /// 自動的に適用する(FR-005〜FR-007)。ベストエフォートで行い、未設定・失敗時はシェル構築自体を止めず、
    /// 詳細な例外はプロセスログにのみ残す(利用者・Run結果には一般化した内容のみ露出する、憲法I)。
    /// 実際に <c>git clone</c> を使わないシェルエージェントの起動を、Git認証の不備でブロックしないための設計。
    /// </summary>
    private void InitializeGitAuthBestEffort()
    {
        if (_gitAuth is null)
        {
            return;
        }

        try
        {
            _gitAuth.InitializeAsync().GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "git auth initialization failed; shell will start without automatic git credentials");
        }
    }

    private static bool IsWorkspace(HarnessAgentConfig config)
        => string.Equals(config.FileStoreKind, "workspace", StringComparison.OrdinalIgnoreCase);

    private static LocalShellExecutor BuildShell(string workDir, WorkspaceYaml? ws)
    {
        var shellOpts = new LocalShellExecutorOptions
        {
            WorkingDirectory = workDir,
            ConfineWorkingDirectory = ws?.Shell?.ConfineWorkingDirectory ?? true,
            Policy = ShellPolicyFactory.Build(ws?.Shell),
            Timeout = ws?.Shell?.TimeoutSeconds is { } sec and > 0 ? TimeSpan.FromSeconds(sec) : null,
            MaxOutputBytes = ws?.Shell?.MaxOutputBytes ?? 131072,
            AcknowledgeUnsafe = true, // 拘束+denylist を認識済みとして危険確認フラグを立てる
        };

        return new LocalShellExecutor(shellOpts);
    }

    private static WorkspaceYaml? LoadWorkspace(HarnessAgentConfig config)
    {
        if (string.IsNullOrWhiteSpace(config.AgentFolderPath))
        {
            return null;
        }

        var path = Path.Combine(config.AgentFolderPath, "workspace.yaml");
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            return WorkspaceYamlSerializer.Deserialize(File.ReadAllText(path));
        }
        catch (Exception ex)
        {
            // workspace.yaml の軽微な記述崩れでエージェント全体を落とさない。既定で続行。
            Debug.Fail("workspace.yaml parse failed: " + ex.Message);
            return null;
        }
    }
}