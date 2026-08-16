using Microsoft.Extensions.Logging;
using WorkAgents.Agents.Configuration;
using WorkAgents.Core.Graphs;

namespace WorkAgents.Agents.Loading;

/// <summary>
/// Loads and validates graph.yaml definitions from the repository. 複数の定義ソースを
/// マージ読み込みする場合は <see cref="LoadAllFromSources"/> を使う(specs/006-team-config-distribution)。
/// </summary>
public sealed class FileBasedGraphLoader
{
    private readonly ILogger<FileBasedGraphLoader>? _logger;

    public FileBasedGraphLoader(ILogger<FileBasedGraphLoader>? logger = null)
    {
        _logger = logger;
    }

    public GraphDefinition Load(
        string graphFolder,
        IReadOnlyCollection<string>? knownAgents = null,
        IReadOnlyCollection<string>? knownTeams = null,
        IReadOnlyCollection<string>? knownGraphs = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(graphFolder);
        var path = Path.Combine(graphFolder, "graph.yaml");
        if (!File.Exists(path))
        {
            throw new GraphYamlValidationException($"graph.yaml not found: {path}");
        }
        return LoadText(File.ReadAllText(path), graphFolder);
    }

    public GraphDefinition LoadText(string yaml, string graphFolder)
    {
        ArgumentNullException.ThrowIfNull(yaml);
        ArgumentException.ThrowIfNullOrWhiteSpace(graphFolder);
        return Convert(GraphYamlSerializer.Deserialize(yaml), graphFolder);
    }

    public IReadOnlyList<GraphDefinition> LoadAll(
        string graphsRoot,
        IReadOnlyCollection<string>? knownAgents = null,
        IReadOnlyCollection<string>? knownTeams = null)
    {
        if (!Directory.Exists(graphsRoot))
        {
            return Array.Empty<GraphDefinition>();
        }
        var graphNames = Directory.EnumerateDirectories(graphsRoot).Select(Path.GetFileName).Where(name => name is not null).Cast<string>().ToArray();
        var results = new List<GraphDefinition>();
        foreach (var name in graphNames)
        {
            try
            {
                results.Add(Load(Path.Combine(graphsRoot, name), knownAgents, knownTeams, graphNames));
            }
            catch (GraphYamlValidationException ex)
            {
                _logger?.LogError(ex, "failed to load graph from {Dir}", Path.Combine(graphsRoot, name));
            }
        }

        return results;
    }

    /// <summary>
    /// 複数の定義ソースを順に走査し、同名グラフを後勝ちでマージ読み込みする(FR-002・FR-005)。
    /// 検証に失敗したグラフはFR-006・FR-007に従いスキップしてログに記録し、読み込みは継続する。
    /// </summary>
    public IReadOnlyList<GraphDefinition> LoadAllFromSources(
        IReadOnlyList<DefinitionSourceEntry> sources,
        IReadOnlyCollection<string>? knownAgents = null,
        IReadOnlyCollection<string>? knownTeams = null,
        ILogger<DefinitionSourceResolver>? resolverLogger = null)
    {
        ArgumentNullException.ThrowIfNull(sources);
        var resolver = new DefinitionSourceResolver(sources, resolverLogger);
        var (folders, _) = resolver.ResolveFolders("graphs");
        var graphNames = folders.Select(folder => folder.Name).ToArray();

        var results = new List<GraphDefinition>();
        foreach (var folder in folders)
        {
            try
            {
                var graph = Load(folder.FolderPath, knownAgents, knownTeams, graphNames);
                results.Add(graph with
                {
                    SourceLabel = folder.SourceLabel,
                    OverriddenSourceLabels = folder.OverriddenSourceLabels,
                });
            }
            catch (GraphYamlValidationException ex)
            {
                _logger?.LogError(ex, "failed to load graph from {Dir} (source={Source})", folder.FolderPath, folder.SourceLabel);
            }
        }

        _logger?.LogInformation("loaded {Count} graph(s) from {SourceCount} source(s)", results.Count, sources.Count);
        return results;
    }

    public static string ResolveGraphsRoot(string baseDir)
        => DefinitionRootResolver.ResolveDirectory(baseDir, "graphs");

    internal static GraphDefinition Convert(GraphYaml yaml, string folderPath)
    {
        var folderName = Path.GetFileName(folderPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var nodes = (yaml.Nodes ?? []).Select(ConvertNode).ToArray();
        var edges = ConvertEdges(yaml.Edges ?? []);
        var subgraphs = (yaml.Subgraphs ?? new Dictionary<string, GraphSubgraphYaml>(StringComparer.Ordinal))
            .ToDictionary(
                pair => pair.Key,
                pair => new SubgraphDefinition
                {
                    Nodes = (pair.Value.Nodes ?? []).Select(ConvertNode).ToArray(),
                    Edges = ConvertEdges(pair.Value.Edges ?? []),
                },
                StringComparer.Ordinal);
        return new GraphDefinition
        {
            Version = yaml.Version ?? 1,
            Name = yaml.Name?.Trim() ?? folderName,
            DisplayName = yaml.DisplayName,
            Description = yaml.Description,
            Defaults = yaml.Defaults is null ? null : new GraphDefaults
            {
                Team = yaml.Defaults.Team,
                BudgetCostLimitUsd = yaml.Defaults.Budget?.CostLimitUsd,
                BudgetTimeLimitSeconds = yaml.Defaults.Budget?.TimeLimitSeconds,
            },
            Nodes = nodes,
            Edges = edges,
            Subgraphs = subgraphs,
            Layout = (yaml.Layout ?? new Dictionary<string, GraphLayoutYaml>(StringComparer.Ordinal))
                .ToDictionary(pair => pair.Key, pair => (pair.Value.X, pair.Value.Y), StringComparer.Ordinal),
            FolderPath = folderPath,
        };
    }

    private static GraphNode ConvertNode(GraphNodeYaml node)
    {
        if (!Enum.TryParse<NodeKind>(node.Kind, true, out var kind))
        {
            kind = (NodeKind)(-1);
        }
        JoinPolicy? join = Enum.TryParse<JoinPolicy>(node.JoinPolicy, true, out var joinValue) ? joinValue : null;
        PartialFailurePolicy? partial = Enum.TryParse<PartialFailurePolicy>(node.OnPartialFailure, true, out var partialValue) ? partialValue : null;
        return new GraphNode
        {
            Id = node.Id?.Trim() ?? string.Empty,
            Kind = kind,
            Agent = node.Agent,
            Team = node.Team,
            Input = node.Input,
            Goal = node.Goal,
            Body = node.Body,
            Stop = node.Stop is null ? null : new LoopStopCondition
            {
                MaxIterations = node.Stop.MaxIterations ?? 10,
                CostLimitUsd = node.Stop.CostLimitUsd,
                TimeLimitSeconds = node.Stop.TimeLimitSeconds,
                ScoreThreshold = node.Stop.ScoreThreshold,
            },
            Evaluator = node.Evaluator is null ? null : new NodeEvaluatorSpec
            {
                Kind = node.Evaluator.Kind ?? string.Empty,
                Node = node.Evaluator.Node,
                Agent = node.Evaluator.Agent,
                Metrics = (node.Evaluator.Metrics ?? []).Select(metric => new MetricTarget
                {
                    Name = metric.Name ?? string.Empty,
                    Target = metric.Target ?? 0,
                    Direction = metric.Direction ?? "gte",
                }).ToArray(),
            },
            Title = node.Title,
            Summary = node.Summary,
            TimeoutSeconds = node.TimeoutSeconds,
            JoinPolicy = join,
            OnPartialFailure = partial,
            Alternate = node.Alternate,
            CodeFile = node.CodeFile,
            Graph = node.Graph,
            Next = node.Next ?? new List<string>(),
        };
    }

    /// <summary>
    /// エッジの id は省略可能。省略時は "&lt;from&gt;-to-&lt;to&gt;" を自動採番する
    /// (同じ from/to の組が複数ある場合は "-2" 以降を付けて一意化する)。
    /// これにより nodes[].next と edges[].id を手作業で対応付ける必要がなくなる。
    /// </summary>
    private static IReadOnlyList<GraphEdge> ConvertEdges(List<GraphEdgeYaml> edges)
    {
        var usedIds = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<GraphEdge>(edges.Count);
        foreach (var edge in edges)
        {
            var from = edge.From?.Trim() ?? string.Empty;
            var to = edge.To?.Trim() ?? string.Empty;
            var id = edge.Id?.Trim();
            if (string.IsNullOrEmpty(id))
            {
                var baseId = $"{from}-to-{to}";
                id = baseId;
                var suffix = 2;
                while (!usedIds.Add(id))
                {
                    id = $"{baseId}-{suffix++}";
                }
            }
            else
            {
                usedIds.Add(id);
            }
            result.Add(new GraphEdge
            {
                Id = id,
                From = from,
                To = to,
                Condition = edge.Condition,
                LoopBack = edge.LoopBack,
            });
        }
        return result;
    }
}
