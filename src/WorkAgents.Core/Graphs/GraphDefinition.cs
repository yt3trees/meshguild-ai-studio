namespace WorkAgents.Core.Graphs;

/// <summary>ノード種別 (FR-026)。</summary>
public enum NodeKind
{
    Agent,
    Team,
    Code,
    Approval,
    Branch,
    Parallel,
    Join,
    Loop,
    Subgraph,
}

/// <summary>join ノードの合流方針。</summary>
public enum JoinPolicy
{
    All,
    Any,
}

/// <summary>join ノードで一部の入力が失敗したときの扱い。</summary>
public enum PartialFailurePolicy
{
    Fail,
    Continue,
    Alternate,
}

/// <summary>loop ノードの停止条件 (FR-020、FR-023)。</summary>
public sealed record LoopStopCondition
{
    public int MaxIterations { get; init; } = 10;

    public double? CostLimitUsd { get; init; }

    public int? TimeLimitSeconds { get; init; }

    public double? ScoreThreshold { get; init; }
}

/// <summary>評価指標 1 件の目標定義。</summary>
public sealed record MetricTarget
{
    public required string Name { get; init; }

    public required double Target { get; init; }

    /// <summary>gte (既定) / lte。</summary>
    public string Direction { get; init; } = "gte";
}

/// <summary>loop ノードの評価者設定 (FR-021)。</summary>
public sealed record NodeEvaluatorSpec
{
    /// <summary>deterministic / agent。</summary>
    public required string Kind { get; init; }

    public string? Node { get; init; }

    public string? Agent { get; init; }

    public IReadOnlyList<MetricTarget> Metrics { get; init; } = Array.Empty<MetricTarget>();
}

/// <summary>1 つのノード定義 (graph.yaml nodes[])。</summary>
public sealed record GraphNode
{
    public required string Id { get; init; }

    public required NodeKind Kind { get; init; }

    public string? Agent { get; init; }

    public string? Team { get; init; }

    public string? Input { get; init; }

    public string? Goal { get; init; }

    public string? Body { get; init; }

    public LoopStopCondition? Stop { get; init; }

    public NodeEvaluatorSpec? Evaluator { get; init; }

    public string? Title { get; init; }

    public string? Summary { get; init; }

    public int? TimeoutSeconds { get; init; }

    public JoinPolicy? JoinPolicy { get; init; }

    public PartialFailurePolicy? OnPartialFailure { get; init; }

    public string? Alternate { get; init; }

    public string? CodeFile { get; init; }

    public string? Graph { get; init; }

    public IReadOnlyList<string> Next { get; init; } = Array.Empty<string>();
}

/// <summary>1 つのエッジ定義 (graph.yaml edges[])。</summary>
public sealed record GraphEdge
{
    public required string Id { get; init; }

    public required string From { get; init; }

    public required string To { get; init; }

    public string? Condition { get; init; }

    public bool LoopBack { get; init; }
}

/// <summary>defaults セクション。</summary>
public sealed record GraphDefaults
{
    public string? Team { get; init; }

    public double? BudgetCostLimitUsd { get; init; }

    public int? BudgetTimeLimitSeconds { get; init; }
}

/// <summary>入れ子グラフ (subgraphs[<id>])。</summary>
public sealed record SubgraphDefinition
{
    public required IReadOnlyList<GraphNode> Nodes { get; init; }

    public required IReadOnlyList<GraphEdge> Edges { get; init; }
}

/// <summary>グラフ定義 (contracts/graph-yaml.md)。真実の源は graph.yaml。</summary>
public sealed record GraphDefinition
{
    public int Version { get; init; } = 1;

    public required string Name { get; init; }

    public string? DisplayName { get; init; }

    public string? Description { get; init; }

    public GraphDefaults? Defaults { get; init; }

    public required IReadOnlyList<GraphNode> Nodes { get; init; }

    public required IReadOnlyList<GraphEdge> Edges { get; init; }

    public IReadOnlyDictionary<string, SubgraphDefinition> Subgraphs { get; init; }
        = new Dictionary<string, SubgraphDefinition>();

    public IReadOnlyDictionary<string, (double X, double Y)> Layout { get; init; }
        = new Dictionary<string, (double X, double Y)>();

    public string FolderPath { get; init; } = string.Empty;

    /// <summary>この定義を採用した定義ソースの <c>Label</c>(data-model.md「解決済み定義」)。</summary>
    public string SourceLabel { get; init; } = "standard";

    /// <summary>同名で存在したが上書きされた側の <c>Label</c>(0件の場合は衝突なし)。</summary>
    public IReadOnlyList<string> OverriddenSourceLabels { get; init; } = Array.Empty<string>();
}

/// <summary>ミッション開始時に固定するグラフ定義のスナップショット (data-model.md GraphVersion)。</summary>
public sealed record GraphVersion
{
    public required string GraphVersionId { get; init; }

    public required string GraphName { get; init; }

    public required int VersionNo { get; init; }

    public required string ContentHash { get; init; }

    public required string DefinitionYaml { get; init; }

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}
