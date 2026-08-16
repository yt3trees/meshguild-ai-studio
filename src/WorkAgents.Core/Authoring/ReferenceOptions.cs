using WorkAgents.Core.Graphs;
using WorkAgents.Core.Teams;

namespace WorkAgents.Core.Authoring;

/// <summary>ドロップダウンの選択肢 1 件。</summary>
public sealed record ReferenceOption(string Value, string? Label = null, string? Detail = null)
{
    public string DisplayLabel => string.IsNullOrWhiteSpace(Label) ? Value : Label!;
}

/// <summary>
/// スキーマの x-source を実際の選択肢に変換する (案A の中心)。
/// 自由入力だった参照項目をすべて選択式にするための解決器で、
/// 「存在しない名前を書いてしまう」という最も多い失敗をフォームの段階で潰す。
/// </summary>
public sealed class ReferenceOptions
{
    public IReadOnlyList<ReferenceOption> Agents { get; init; } = Array.Empty<ReferenceOption>();

    public IReadOnlyList<ReferenceOption> Teams { get; init; } = Array.Empty<ReferenceOption>();

    public IReadOnlyList<ReferenceOption> Graphs { get; init; } = Array.Empty<ReferenceOption>();

    public IReadOnlyList<ReferenceOption> Skills { get; init; } = Array.Empty<ReferenceOption>();

    /// <summary>編集中のグラフ。nodes / subgraphs / code-nodes の解決に使う。</summary>
    public GraphDefinition? Graph { get; init; }

    /// <summary>編集中のチーム。team-agents の解決に使う。</summary>
    public TeamDefinition? Team { get; init; }

    /// <summary>
    /// x-source の値から選択肢を引く。未知の source は空を返す
    /// (GUI 側は空なら自由入力にフォールバックする)。
    /// </summary>
    public IReadOnlyList<ReferenceOption> For(string? source)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return Array.Empty<ReferenceOption>();
        }

        return source switch
        {
            "agents" => Agents,
            "teams" => Teams,
            "graphs" => Graphs,
            "skills" => Skills,
            "nodes" => NodeOptions(node => true),
            "code-nodes" => NodeOptions(node => node.Kind == NodeKind.Code),
            "subgraphs" => SubgraphOptions(),
            "team-agents" => TeamAgentOptions(),
            _ => Array.Empty<ReferenceOption>(),
        };
    }

    /// <summary>指定した値が選択肢に含まれているか。既存 YAML の参照切れ検出に使う。</summary>
    public bool Contains(string? source, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }
        var options = For(source);
        return options.Count == 0 || options.Any(option => string.Equals(option.Value, value, StringComparison.OrdinalIgnoreCase));
    }

    private IReadOnlyList<ReferenceOption> NodeOptions(Func<GraphNode, bool> predicate)
    {
        if (Graph is null)
        {
            return Array.Empty<ReferenceOption>();
        }
        return Graph.Nodes
            .Where(predicate)
            .Select(node => new ReferenceOption(node.Id, node.Id, node.Kind.ToString().ToLowerInvariant()))
            .ToArray();
    }

    private IReadOnlyList<ReferenceOption> SubgraphOptions()
    {
        if (Graph is null)
        {
            return Array.Empty<ReferenceOption>();
        }
        return Graph.Subgraphs
            .Select(pair => new ReferenceOption(pair.Key, pair.Key, $"ノード {pair.Value.Nodes.Count} 件"))
            .ToArray();
    }

    private IReadOnlyList<ReferenceOption> TeamAgentOptions()
    {
        if (Team is null)
        {
            return Array.Empty<ReferenceOption>();
        }
        var options = new List<ReferenceOption>
        {
            new(Team.Orchestrator.Agent, Team.Orchestrator.Agent, "統括"),
        };
        options.AddRange(Team.Members.Select(member =>
            new ReferenceOption(member.Agent, member.Agent, string.IsNullOrWhiteSpace(member.Role) ? "メンバー" : member.Role)));
        return options;
    }

    public static ReferenceOptions Empty { get; } = new();
}
