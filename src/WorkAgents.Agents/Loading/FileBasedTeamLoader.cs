using Microsoft.Extensions.Logging;
using WorkAgents.Agents.Configuration;
using WorkAgents.Core.Missions;
using WorkAgents.Core.Teams;

namespace WorkAgents.Agents.Loading;

/// <summary>
/// ファイルベースチームローダ (T037)。<c>teams/&lt;name&gt;/team.yaml</c> を読み、
/// contracts/team-yaml.md の全検証規則を適用して <see cref="TeamDefinition"/> を構築する。
/// 複数の定義ソースをマージ読み込みする場合は <see cref="LoadAllFromSources"/> を使う
/// (specs/006-team-config-distribution)。
/// </summary>
public sealed class FileBasedTeamLoader
{
    private static readonly IReadOnlyDictionary<string, MessageKind> KnownKinds =
        Enum.GetValues<MessageKind>().ToDictionary(
            k => ToLowerCamel(k.ToString()),
            k => k,
            StringComparer.Ordinal);

    private readonly ILogger<FileBasedTeamLoader>? _logger;

    public FileBasedTeamLoader(ILogger<FileBasedTeamLoader>? logger = null)
    {
        _logger = logger;
    }

    /// <summary>1 件のチーム定義を読み込む。<paramref name="knownAgentNames"/> は既存エージェント名一覧。</summary>
    public TeamDefinition Load(string teamFolder, IReadOnlyCollection<string> knownAgentNames)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(teamFolder);
        ArgumentNullException.ThrowIfNull(knownAgentNames);

        var yamlPath = Path.Combine(teamFolder, "team.yaml");
        if (!File.Exists(yamlPath))
        {
            throw new TeamValidationException($"team.yaml not found: {yamlPath}");
        }

        var yamlText = File.ReadAllText(yamlPath);
        var yaml = TeamYamlSerializer.Deserialize(yamlText);
        var folderName = Path.GetFileName(teamFolder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

        return Validate(yaml, folderName, knownAgentNames, teamFolder);
    }

    /// <summary>複数チームを読み込む。個別の失敗はスキップして返却リストに含めない。</summary>
    public IReadOnlyList<TeamDefinition> LoadAll(string teamsRoot, IReadOnlyCollection<string> knownAgentNames)
    {
        var results = new List<TeamDefinition>();
        if (!Directory.Exists(teamsRoot))
        {
            return results;
        }

        foreach (var dir in Directory.EnumerateDirectories(teamsRoot))
        {
            if (!File.Exists(Path.Combine(dir, "team.yaml")))
            {
                continue;
            }

            try
            {
                results.Add(Load(dir, knownAgentNames));
            }
            catch (TeamValidationException ex)
            {
                _logger?.LogError(ex, "failed to load team from {Dir}", dir);
            }
        }

        return results;
    }

    /// <summary>
    /// 複数の定義ソースを順に走査し、同名チームを後勝ちでマージ読み込みする(FR-002・FR-005)。
    /// 検証に失敗したチームはFR-006・FR-007に従いスキップしてログに記録し、読み込みは継続する。
    /// </summary>
    public IReadOnlyList<TeamDefinition> LoadAllFromSources(
        IReadOnlyList<DefinitionSourceEntry> sources,
        IReadOnlyCollection<string> knownAgentNames,
        ILogger<DefinitionSourceResolver>? resolverLogger = null)
    {
        ArgumentNullException.ThrowIfNull(sources);
        var resolver = new DefinitionSourceResolver(sources, resolverLogger);
        var (folders, _) = resolver.ResolveFolders("teams");

        var results = new List<TeamDefinition>();
        foreach (var folder in folders)
        {
            try
            {
                var team = Load(folder.FolderPath, knownAgentNames);
                results.Add(team with
                {
                    SourceLabel = folder.SourceLabel,
                    OverriddenSourceLabels = folder.OverriddenSourceLabels,
                });
            }
            catch (TeamValidationException ex)
            {
                _logger?.LogError(ex, "failed to load team from {Dir} (source={Source})", folder.FolderPath, folder.SourceLabel);
            }
        }

        _logger?.LogInformation("loaded {Count} team(s) from {SourceCount} source(s)", results.Count, sources.Count);
        return results;
    }

    private static ChannelDefault ParseChannelsDefault(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return ChannelDefault.ViaOrchestrator;
        }

        return value switch
        {
            "via-orchestrator" => ChannelDefault.ViaOrchestrator,
            "direct" => ChannelDefault.Direct,
            _ => throw new TeamValidationException($"unknown channels.default: {value}"),
        };
    }

    private static TeamDefinition Validate(
        TeamYaml yaml,
        string folderName,
        IReadOnlyCollection<string> knownAgentNames,
        string folderPath)
    {
        var version = yaml.Version ?? 1;
        if (version != 1)
        {
            throw new TeamValidationException("unsupported team.yaml version");
        }

        var name = yaml.Name?.Trim();
        if (string.IsNullOrWhiteSpace(name) || !string.Equals(name, folderName, StringComparison.Ordinal))
        {
            throw new TeamValidationException("team name must match folder name");
        }

        if (yaml.Orchestrator is null || string.IsNullOrWhiteSpace(yaml.Orchestrator.Agent))
        {
            throw new TeamValidationException("team must have an orchestrator");
        }

        var orchestratorAgent = yaml.Orchestrator.Agent!.Trim();
        if (!knownAgentNames.Contains(orchestratorAgent, StringComparer.OrdinalIgnoreCase))
        {
            throw new TeamValidationException($"unknown agent: {orchestratorAgent}");
        }

        if (yaml.Members is null || yaml.Members.Count == 0)
        {
            throw new TeamValidationException("team must have at least one member");
        }

        var members = new List<TeamMember>();
        var seenMembers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var teamAgentNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { orchestratorAgent };

        foreach (var memberYaml in yaml.Members)
        {
            var agent = memberYaml.Agent?.Trim();
            if (string.IsNullOrWhiteSpace(agent))
            {
                throw new TeamValidationException("member requires an agent name");
            }

            if (!knownAgentNames.Contains(agent, StringComparer.OrdinalIgnoreCase))
            {
                throw new TeamValidationException($"unknown agent: {agent}");
            }

            if (!seenMembers.Add(agent))
            {
                throw new TeamValidationException($"duplicate member: {agent}");
            }

            teamAgentNames.Add(agent);
            members.Add(new TeamMember
            {
                Agent = agent,
                Role = string.IsNullOrWhiteSpace(memberYaml.Role) ? null : memberYaml.Role!.Trim(),
                Scope = string.IsNullOrWhiteSpace(memberYaml.Scope) ? null : memberYaml.Scope!.Trim(),
                MaxInstances = memberYaml.MaxInstances ?? 1,
            });
        }

        var channelsDefault = ParseChannelsDefault(yaml.Channels?.Default);
        var channelRules = new List<ChannelRule>();
        if (yaml.Channels?.Allow is { Count: > 0 })
        {
            foreach (var allow in yaml.Channels.Allow)
            {
                var from = allow.From?.Trim();
                var to = allow.To?.Trim();
                if (string.IsNullOrWhiteSpace(from) || !teamAgentNames.Contains(from) ||
                    string.IsNullOrWhiteSpace(to) || !teamAgentNames.Contains(to))
                {
                    throw new TeamValidationException("channel refers to an agent outside the team");
                }

                var kinds = new List<MessageKind>();
                foreach (var kindText in allow.Kinds ?? new List<string>())
                {
                    if (!KnownKinds.TryGetValue(kindText, out var kind))
                    {
                        throw new TeamValidationException($"unknown message kind: {kindText}");
                    }
                    kinds.Add(kind);
                }

                channelRules.Add(new ChannelRule { From = from!, To = to!, Kinds = kinds });
            }
        }

        var limitsYaml = yaml.Limits ?? new TeamLimitsYaml();
        var maxDelegationDepth = limitsYaml.MaxDelegationDepth ?? 3;
        if (maxDelegationDepth is < 1 or > 10)
        {
            throw new TeamValidationException("maxDelegationDepth out of range");
        }

        var maxParallelInstances = limitsYaml.MaxParallelInstances ?? 6;
        var totalInstances = (yaml.Orchestrator.MaxInstances ?? 1) + members.Sum(m => m.MaxInstances);
        if (totalInstances > maxParallelInstances)
        {
            throw new TeamValidationException("member instances exceed team parallel limit");
        }

        var limits = new TeamLimits
        {
            MaxDelegationDepth = maxDelegationDepth,
            MaxParallelInstances = maxParallelInstances,
            NoProgressRoundTrips = limitsYaml.NoProgressRoundTrips ?? 5,
            AskTimeoutSeconds = limitsYaml.AskTimeoutSeconds ?? 300,
        };

        TeamEvaluationDefaults? evaluation = null;
        if (yaml.Evaluation is not null)
        {
            if (yaml.Evaluation.ScoreThreshold is < 0.0 or > 1.0)
            {
                throw new TeamValidationException("scoreThreshold out of range");
            }

            evaluation = new TeamEvaluationDefaults
            {
                Evaluator = string.IsNullOrWhiteSpace(yaml.Evaluation.Evaluator) ? null : yaml.Evaluation.Evaluator!.Trim(),
                ScoreThreshold = yaml.Evaluation.ScoreThreshold,
            };
        }

        return new TeamDefinition
        {
            Version = version,
            Name = name!,
            DisplayName = string.IsNullOrWhiteSpace(yaml.DisplayName) ? null : yaml.DisplayName!.Trim(),
            Description = string.IsNullOrWhiteSpace(yaml.Description) ? null : yaml.Description!.Trim(),
            Orchestrator = new TeamOrchestrator
            {
                Agent = orchestratorAgent,
                MaxInstances = yaml.Orchestrator.MaxInstances ?? 1,
            },
            Members = members,
            ChannelsDefault = channelsDefault,
            ChannelsAllow = channelRules,
            Limits = limits,
            Evaluation = evaluation,
            FolderPath = folderPath,
        };
    }

    private static string ToLowerCamel(string pascalCase)
        => string.IsNullOrEmpty(pascalCase) ? pascalCase : char.ToLowerInvariant(pascalCase[0]) + pascalCase[1..];

    /// <summary>参照ディレクトリを解決するヘルパ(実行ホストで使い分け)。</summary>
    public static string ResolveTeamsRoot(string baseDir)
        => DefinitionRootResolver.ResolveDirectory(baseDir, "teams");
}
