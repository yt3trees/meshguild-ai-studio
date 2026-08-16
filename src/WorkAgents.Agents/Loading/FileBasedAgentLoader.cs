using Microsoft.Extensions.Logging;
using WorkAgents.Agents.Configuration;

namespace WorkAgents.Agents.Loading;

/// <summary>
/// ファイルベースエージェントローダ(5.2)。<c>agents/</c> を走査し、各フォルダから
/// <c>agent.yaml</c> + <c>instructions.md</c> を読んで <see cref="AgentDefinition"/> を構築する。
/// MAF 本体はフォルダ自動検出しないため、本ローダで吸収する(第4章「設計上の重要な非対称」)。
/// tools/*.cs は通常のアセンブリコードとしてコンパイルされ、AgentToolCatalog がProviderを起動時に登録する。
/// 複数の定義ソース(共通システム標準+チーム定義パッケージ)をマージ読み込みする場合は
/// <see cref="LoadFromSources"/> を使う(specs/006-team-config-distribution)。
/// </summary>
public sealed class FileBasedAgentLoader
{
    private readonly string _agentsRoot;
    private readonly ILogger<FileBasedAgentLoader>? _logger;

    public FileBasedAgentLoader(string agentsRoot, ILogger<FileBasedAgentLoader>? logger = null)
    {
        _agentsRoot = agentsRoot;
        _logger = logger;
    }

    public IReadOnlyList<AgentDefinition> Load()
    {
        var list = new List<AgentDefinition>();
        var sourceRoot = Path.GetFullPath(Path.Combine(_agentsRoot, ".."));
        var sharedSkillLocations = SharedSkillCatalog.ResolveLocations([
            new DefinitionSourceEntry { Label = "standard", Path = sourceRoot },
        ]).ToDictionary(location => location.Name, StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(_agentsRoot))
        {
            _logger?.LogWarning("agents root not found: {Root}", _agentsRoot);
            return list;
        }

        foreach (var dir in Directory.EnumerateDirectories(_agentsRoot))
        {
            var definition = BuildDefinition(dir, sharedSkillLocations, "standard", Array.Empty<string>());
            if (definition is not null)
            {
                list.Add(definition);
            }
        }

        _logger?.LogInformation("loaded {Count} agent(s) from {Root}", list.Count, _agentsRoot);
        return list;
    }

    /// <summary>
    /// 複数の定義ソースを順に走査し、同名エージェントを後勝ちでマージ読み込みする
    /// (data-model.md「定義ソース構成」、FR-002・FR-005)。<c>sources[].Path</c> 配下の
    /// <c>agents/</c> サブディレクトリが対象。共有スキルも全ソースから後勝ちで解決する。
    /// </summary>
    public IReadOnlyList<AgentDefinition> LoadFromSources(
        IReadOnlyList<DefinitionSourceEntry> sources,
        ILogger<DefinitionSourceResolver>? resolverLogger = null)
    {
        ArgumentNullException.ThrowIfNull(sources);
        var resolver = new DefinitionSourceResolver(sources, resolverLogger);
        var (folders, _) = resolver.ResolveFolders("agents");
        var sharedSkillLocations = SharedSkillCatalog.ResolveLocations(sources)
            .ToDictionary(location => location.Name, StringComparer.OrdinalIgnoreCase);

        var list = new List<AgentDefinition>();
        foreach (var folder in folders)
        {
            var definition = BuildDefinition(folder.FolderPath, sharedSkillLocations, folder.SourceLabel, folder.OverriddenSourceLabels);
            if (definition is not null)
            {
                list.Add(definition);
            }
        }

        _logger?.LogInformation("loaded {Count} agent(s) from {SourceCount} source(s)", list.Count, sources.Count);
        return list;
    }

    private AgentDefinition? BuildDefinition(
        string dir,
        IReadOnlyDictionary<string, SharedSkillLocation> sharedSkillLocations,
        string sourceLabel,
        IReadOnlyList<string> overriddenSourceLabels)
    {
        var yamlPath = Path.Combine(dir, "agent.yaml");
        if (!File.Exists(yamlPath))
        {
            return null;
        }

        try
        {
            var yamlText = File.ReadAllText(yamlPath);
            var yaml = AgentYamlSerializer.Deserialize(yamlText);
            var instructions = ReadInstructions(dir);
            var name = string.IsNullOrWhiteSpace(yaml.Name) ? Path.GetFileName(dir) : yaml.Name!.Trim();
            var sharedSkillNames = ResolveSharedSkillNames(yaml.Skills, sharedSkillLocations, name);

            return new AgentDefinition
            {
                Name = name,
                Kind = string.IsNullOrWhiteSpace(yaml.Kind) ? null : yaml.Kind!.Trim(),
                DisplayName = string.IsNullOrWhiteSpace(yaml.DisplayName)
                    ? (yaml.Name?.Trim() ?? Path.GetFileName(dir))
                    : yaml.DisplayName!.Trim(),
                Description = yaml.Description ?? "",
                Instructions = instructions,
                FolderPath = dir,
                SharedSkillNames = sharedSkillNames,
                SharedSkillPaths = sharedSkillNames.ToDictionary(
                    skillName => skillName,
                    skillName => sharedSkillLocations[skillName].FolderPath,
                    StringComparer.OrdinalIgnoreCase),
                LocalSkillNames = FindLocalSkillNames(dir),
                HarnessShell = yaml.Harness?.Shell ?? false,
                HarnessFileStore = string.IsNullOrWhiteSpace(yaml.Harness?.FileStore) ? null : yaml.Harness!.FileStore!.Trim(),
                SourceLabel = sourceLabel,
                OverriddenSourceLabels = overriddenSourceLabels,
            };
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "failed to load agent from {Dir}", dir);
            return null;
        }
    }

    private static string ReadInstructions(string dir)
    {
        var path = Path.Combine(dir, "instructions.md");
        return File.Exists(path) ? File.ReadAllText(path) : "";
    }

    private IReadOnlyList<string> ResolveSharedSkillNames(
        IEnumerable<string>? configuredNames,
        IReadOnlyDictionary<string, SharedSkillLocation> sharedSkillLocations,
        string agentName)
    {
        if (configuredNames is null)
        {
            return Array.Empty<string>();
        }

        var names = new List<string>();
        foreach (var configuredName in configuredNames)
        {
            var name = configuredName?.Trim();
            if (string.IsNullOrWhiteSpace(name) || !IsSkillName(name))
            {
                _logger?.LogWarning("agent {Agent} configured an invalid shared skill name: {Skill}", agentName, configuredName);
                continue;
            }

            if (!sharedSkillLocations.ContainsKey(name))
            {
                _logger?.LogWarning("agent {Agent} configured shared skill {Skill}, but no SKILL.md was found in the configured definition sources", agentName, name);
                continue;
            }

            if (!names.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                names.Add(name);
            }
        }

        return names;
    }

    private static IReadOnlyList<string> FindLocalSkillNames(string agentDirectory)
    {
        var root = Path.Combine(agentDirectory, "skills");
        if (!Directory.Exists(root))
        {
            return Array.Empty<string>();
        }

        return Directory.EnumerateDirectories(root)
            .Where(directory => File.Exists(Path.Combine(directory, "SKILL.md")))
            .Select(Path.GetFileName)
            .Where(name => !string.IsNullOrWhiteSpace(name) && IsSkillName(name))
            .Cast<string>()
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool IsSkillName(string name)
        => string.Equals(name, Path.GetFileName(name), StringComparison.Ordinal)
            && name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;

    /// <summary>参照ディレクトリを解決するヘルパ(実行ホストで使い分け)。</summary>
    public static string ResolveAgentsRoot(string baseDir)
        => DefinitionRootResolver.ResolveDirectory(baseDir, "agents");

    /// <summary>
    /// 標準ソースの読み込み元ルート(<c>agents/</c> 等のサブフォルダの親)を解決する。
    /// 開発時はプロセス出力フォルダー、publish時は兄弟の<c>definitions/</c>を使う。
    /// </summary>
    public static string ResolveStandardSourceRoot(string baseDir)
        => DefinitionRootResolver.ResolveSourceRoot(baseDir);
}
