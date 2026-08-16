using System.Globalization;
using System.Text;
using WorkAgents.Core.Graphs;
using WorkAgents.Core.Missions;
using WorkAgents.Core.Teams;

namespace WorkAgents.Core.Authoring;

/// <summary>要約 1 行。<see cref="Detail"/> は補足で、無い場合もある。</summary>
public sealed record NarrationLine(string Heading, string Text);

/// <summary>
/// 定義から「これはこう動きます」という日本語の説明を組み立てる (案C)。
/// 書き手が自分の書いた YAML を検算するため、およびレビューする側が読むために使う。
/// 表示専用であり、実行エンジンはこの出力を参照しない。
/// </summary>
public static class DefinitionNarrator
{
    // ---------------------------------------------------------------- team

    /// <summary>チーム 1 件を 1 行にまとめる。一覧表示向け。</summary>
    public static string Headline(TeamDefinition team)
    {
        ArgumentNullException.ThrowIfNull(team);
        var channel = team.ChannelsDefault == ChannelDefault.Direct
            ? "メンバー同士が直接会話する"
            : "会話はすべて統括を経由する";
        return $"統括 {team.Orchestrator.Agent} が {team.Members.Count} 名に委譲する。{channel}。";
    }

    /// <summary>チーム定義を節ごとの説明に展開する。</summary>
    public static IReadOnlyList<NarrationLine> Describe(TeamDefinition team)
    {
        ArgumentNullException.ThrowIfNull(team);
        var lines = new List<NarrationLine>();

        var members = team.Members
            .Select(member => string.IsNullOrWhiteSpace(member.Role)
                ? member.Agent
                : $"{member.Agent} ({member.Role})")
            .ToArray();
        var roster = new StringBuilder();
        roster.Append($"統括は {team.Orchestrator.Agent}");
        if (team.Orchestrator.MaxInstances > 1)
        {
            roster.Append($" (最大 {team.Orchestrator.MaxInstances} 体)");
        }
        roster.Append("。サブエージェントは ");
        roster.Append(members.Length == 0 ? "なし" : string.Join("、", members));
        roster.Append($" の {team.Members.Count} 名。");
        var multi = team.Members.Where(member => member.MaxInstances > 1).ToArray();
        if (multi.Length > 0)
        {
            roster.Append("このうち ");
            roster.Append(string.Join("、", multi.Select(member => $"{member.Agent} は最大 {member.MaxInstances} 体")));
            roster.Append(" まで同時に動く。");
        }
        lines.Add(new NarrationLine("編成", roster.ToString()));

        var scoped = team.Members.Where(member => !string.IsNullOrWhiteSpace(member.Scope)).ToArray();
        if (scoped.Length > 0)
        {
            lines.Add(new NarrationLine(
                "担当範囲",
                string.Join("、", scoped.Select(member => $"{member.Agent} は {member.Scope}"))));
        }

        var channels = new StringBuilder();
        channels.Append(team.ChannelsDefault == ChannelDefault.Direct
            ? "既定でメンバー同士が直接会話できる。"
            : "既定ではすべての会話が統括を経由する。");
        if (team.ChannelsAllow.Count > 0)
        {
            channels.Append("加えて ");
            channels.Append(string.Join("、", team.ChannelsAllow.Select(DescribeChannel)));
            channels.Append(" が直接やり取りできる。");
        }
        else if (team.ChannelsDefault == ChannelDefault.ViaOrchestrator)
        {
            channels.Append("直接会話の例外は設定されていない。");
        }
        lines.Add(new NarrationLine("会話", channels.ToString()));

        lines.Add(new NarrationLine(
            "上限",
            $"委譲は {team.Limits.MaxDelegationDepth} 段まで、同時に動くエージェントは {team.Limits.MaxParallelInstances} 体まで。" +
            $"進展のない往復が {team.Limits.NoProgressRoundTrips} 回続いたら実行を止める。質問の返答待ちは最大 {team.Limits.AskTimeoutSeconds} 秒。"));

        if (team.Evaluation is not null)
        {
            var evaluation = new StringBuilder();
            evaluation.Append(string.IsNullOrWhiteSpace(team.Evaluation.Evaluator)
                ? "評価者エージェントは指定されておらず、決定的な評価だけを行う。"
                : $"評価は {team.Evaluation.Evaluator} が担当する。");
            if (team.Evaluation.ScoreThreshold is { } threshold)
            {
                evaluation.Append($"スコアが {Number(threshold)} 以上になったら停止する。");
            }
            lines.Add(new NarrationLine("評価", evaluation.ToString()));
        }

        return lines;
    }

    private static string DescribeChannel(ChannelRule rule)
    {
        var kinds = rule.Kinds.Count == 0
            ? "すべての種別"
            : string.Join("と", rule.Kinds.Select(KindLabel));
        return $"{rule.From} から {rule.To} への{kinds}";
    }

    private static string KindLabel(MessageKind kind) => kind switch
    {
        MessageKind.Question => "質問",
        MessageKind.Answer => "回答",
        MessageKind.Share => "共有",
        _ => kind.ToString(),
    };

    // --------------------------------------------------------------- graph

    /// <summary>グラフ 1 件を 1 行にまとめる。一覧表示向け。</summary>
    public static string Headline(GraphDefinition graph)
    {
        ArgumentNullException.ThrowIfNull(graph);
        var start = StartNodes(graph).FirstOrDefault();
        var startText = start is null ? "開始ノードを特定できない" : $"{start.Id} から始まる";
        return $"ノード {graph.Nodes.Count} 件、エッジ {graph.Edges.Count} 件。{startText}。";
    }

    /// <summary>グラフ定義を、ノードごとの説明と経路の説明に展開する。</summary>
    public static IReadOnlyList<NarrationLine> Describe(GraphDefinition graph)
    {
        ArgumentNullException.ThrowIfNull(graph);
        var lines = new List<NarrationLine>();

        var starts = StartNodes(graph).Select(node => node.Id).ToArray();
        lines.Add(new NarrationLine(
            "開始",
            starts.Length == 0
                ? "どのノードにも入力エッジがあり、開始点を特定できない。"
                : $"{string.Join("、", starts)} から始まる。"));

        foreach (var node in OrderedNodes(graph))
        {
            lines.Add(new NarrationLine(node.Id, DescribeNode(node, graph)));
        }

        var terminals = graph.Nodes
            .Where(node => !graph.Edges.Any(edge => !edge.LoopBack && edge.From == node.Id))
            .Select(node => node.Id)
            .ToArray();
        if (terminals.Length > 0)
        {
            lines.Add(new NarrationLine("終了", $"{string.Join("、", terminals)} まで進んだら終了する。"));
        }

        if (graph.Defaults is not null)
        {
            var defaults = new List<string>();
            if (!string.IsNullOrWhiteSpace(graph.Defaults.Team))
            {
                defaults.Add($"team を省略したノードは {graph.Defaults.Team} を使う");
            }
            if (graph.Defaults.BudgetCostLimitUsd is { } cost)
            {
                defaults.Add($"コスト上限は {Number(cost)} USD");
            }
            if (graph.Defaults.BudgetTimeLimitSeconds is { } time)
            {
                defaults.Add($"実行時間上限は {time} 秒");
            }
            if (defaults.Count > 0)
            {
                lines.Add(new NarrationLine("既定値", string.Join("、", defaults) + "。"));
            }
        }

        return lines;
    }

    /// <summary>ノード 1 件のふるまいと、その後どこへ進むかを 1 文にする。</summary>
    public static string DescribeNode(GraphNode node, GraphDefinition graph)
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(graph);

        var body = node.Kind switch
        {
            NodeKind.Agent =>
                $"エージェント {Or(node.Agent, "(未設定)")} に{Quoted(node.Input, "入力なしで")}任せる。",

            NodeKind.Team =>
                $"チーム {Or(node.Team, graph.Defaults?.Team ?? "(未設定)")} に{Quoted(node.Goal, "目標なしで")}渡す。",

            NodeKind.Code =>
                $"スクリプト {Or(node.CodeFile, "(未設定)")} を実行する。",

            NodeKind.Approval =>
                $"人の承認を待つ ({Or(node.Title, "タイトル未設定")}、最大 {node.TimeoutSeconds ?? 900} 秒)。",

            NodeKind.Branch =>
                "条件を見て経路を分ける。",

            NodeKind.Parallel =>
                "ここから複数の経路へ同時に流す。",

            NodeKind.Join =>
                DescribeJoin(node),

            NodeKind.Loop =>
                DescribeLoop(node),

            NodeKind.Subgraph =>
                $"別のグラフ {Or(node.Graph, "(未設定)")} を呼び出す。",

            _ => "種別が不明。",
        };

        return body + " " + DescribeOutgoing(node, graph);
    }

    private static string DescribeJoin(GraphNode node)
    {
        var policy = node.JoinPolicy switch
        {
            Graphs.JoinPolicy.All => "すべての入力が揃うまで待つ",
            Graphs.JoinPolicy.Any => "最初に届いた 1 件で先へ進む",
            _ => "合流方針が未設定",
        };
        var failure = node.OnPartialFailure switch
        {
            PartialFailurePolicy.Fail => "。一部でも失敗したらグラフ全体を失敗にする",
            PartialFailurePolicy.Continue => "。一部が失敗しても成功した分だけで先へ進む",
            PartialFailurePolicy.Alternate => $"。一部が失敗したら {Or(node.Alternate, "(迂回先未設定)")} へ迂回する",
            _ => string.Empty,
        };
        return $"並列経路を合流させ、{policy}{failure}。";
    }

    private static string DescribeLoop(GraphNode node)
    {
        var builder = new StringBuilder();
        builder.Append($"サブグラフ {Or(node.Body, "(未設定)")} を繰り返す。");

        var stops = new List<string>();
        if (node.Stop is { } stop)
        {
            if (stop.MaxIterations > 0) stops.Add($"{stop.MaxIterations} 回まで");
            if (stop.ScoreThreshold is { } score) stops.Add($"スコアが {Number(score)} 以上になったら");
            if (stop.CostLimitUsd is { } cost) stops.Add($"コストが {Number(cost)} USD に達したら");
            if (stop.TimeLimitSeconds is { } time) stops.Add($"累計 {time} 秒に達したら");
        }
        builder.Append(stops.Count == 0
            ? "停止条件が未設定のため止まらない。"
            : $"{string.Join("、", stops)}停止する。");

        if (node.Evaluator is { } evaluator)
        {
            if (string.Equals(evaluator.Kind, "agent", StringComparison.OrdinalIgnoreCase))
            {
                builder.Append($"採点は {Or(evaluator.Agent, "(未設定)")} が行う。");
            }
            else
            {
                var metrics = evaluator.Metrics.Count == 0
                    ? "指標は未設定"
                    : string.Join("、", evaluator.Metrics.Select(metric =>
                        $"{metric.Name} が {Number(metric.Target)} {(string.Equals(metric.Direction, "lte", StringComparison.OrdinalIgnoreCase) ? "以下" : "以上")}"));
                builder.Append($"判定は {Or(evaluator.Node, "(code ノード未設定)")} の出力で行い、{metrics}であれば合格。");
            }
        }

        return builder.ToString();
    }

    private static string DescribeOutgoing(GraphNode node, GraphDefinition graph)
    {
        var outgoing = graph.Edges.Where(edge => edge.From == node.Id).ToArray();
        if (outgoing.Length == 0)
        {
            return "この先はなく、ここで終わる。";
        }

        var parts = outgoing.Select(edge =>
        {
            var target = edge.LoopBack ? $"{edge.To} へ戻る" : $"{edge.To} へ進む";
            return string.IsNullOrWhiteSpace(edge.Condition)
                ? target
                : $"{edge.Condition} が真なら {target}";
        });

        var defaultEdges = outgoing.Count(edge => string.IsNullOrWhiteSpace(edge.Condition));
        var suffix = node.Kind == NodeKind.Branch && defaultEdges == 0
            ? " どの条件にも当てはまらなかったときの行き先が無い。"
            : string.Empty;

        return $"その後は{string.Join("、", parts)}。{suffix}".TrimEnd();
    }

    // --------------------------------------------------------------- 共通

    /// <summary>入力エッジ (loopBack を除く) を持たないノード。</summary>
    private static IEnumerable<GraphNode> StartNodes(GraphDefinition graph)
    {
        var incoming = graph.Edges.Where(edge => !edge.LoopBack).Select(edge => edge.To).ToHashSet(StringComparer.Ordinal);
        return graph.Nodes.Where(node => !incoming.Contains(node.Id));
    }

    /// <summary>開始ノードから幅優先で辿った順。到達できないノードは末尾に付ける。</summary>
    private static IReadOnlyList<GraphNode> OrderedNodes(GraphDefinition graph)
    {
        var byId = graph.Nodes.ToDictionary(node => node.Id, StringComparer.Ordinal);
        var ordered = new List<GraphNode>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var queue = new Queue<string>(StartNodes(graph).Select(node => node.Id));

        while (queue.Count > 0)
        {
            var id = queue.Dequeue();
            if (!seen.Add(id) || !byId.TryGetValue(id, out var node))
            {
                continue;
            }
            ordered.Add(node);
            foreach (var edge in graph.Edges.Where(edge => !edge.LoopBack && edge.From == id))
            {
                queue.Enqueue(edge.To);
            }
        }

        ordered.AddRange(graph.Nodes.Where(node => !seen.Contains(node.Id)));
        return ordered;
    }

    private static string Or(string? value, string fallback)
        => string.IsNullOrWhiteSpace(value) ? fallback : value;

    private static string Quoted(string? value, string fallback)
        => string.IsNullOrWhiteSpace(value) ? fallback : $"「{Shorten(value)}」を渡して";

    private static string Shorten(string value)
    {
        var single = value.ReplaceLineEndings(" ").Trim();
        return single.Length <= 60 ? single : single[..60] + "…";
    }

    private static string Number(double value)
        => value.ToString("0.###", CultureInfo.InvariantCulture);
}
