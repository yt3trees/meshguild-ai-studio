using System.Globalization;
using System.Text;
using WorkAgents.Core.Graphs;

namespace WorkAgents.Agents.Loading;

/// <summary>
/// 編集された <see cref="GraphDefinition"/> を graph.yaml へ書き戻す。
/// GUI からの保存で項目が落ちないよう、<see cref="GraphDefinition"/> が持つ値はすべて出力する
/// (subgraphs、evaluator、timeoutSeconds を含む)。
/// なお YAML のコメントは保持されない。GUI で保存すると元ファイルのコメントは失われる。
/// </summary>
public sealed class GraphYamlWriter
{
    public Task WriteAsync(GraphDefinition graph, string path, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }
        return File.WriteAllTextAsync(path, ToYaml(graph), Encoding.UTF8, ct);
    }

    public string ToYaml(GraphDefinition graph)
    {
        ArgumentNullException.ThrowIfNull(graph);
        var builder = new StringBuilder();
        builder.AppendLine($"version: {graph.Version}");
        builder.AppendLine($"name: {Quote(graph.Name)}");
        if (!string.IsNullOrWhiteSpace(graph.DisplayName)) builder.AppendLine($"displayName: {Quote(graph.DisplayName!)}");
        if (!string.IsNullOrWhiteSpace(graph.Description)) builder.AppendLine($"description: {Quote(graph.Description!)}");
        if (graph.Defaults is not null)
        {
            builder.AppendLine("defaults:");
            if (!string.IsNullOrWhiteSpace(graph.Defaults.Team)) builder.AppendLine($"  team: {Quote(graph.Defaults.Team!)}");
            if (graph.Defaults.BudgetCostLimitUsd.HasValue || graph.Defaults.BudgetTimeLimitSeconds.HasValue)
            {
                builder.AppendLine("  budget:");
                if (graph.Defaults.BudgetCostLimitUsd.HasValue) builder.AppendLine($"    costLimitUsd: {Number(graph.Defaults.BudgetCostLimitUsd.Value)}");
                if (graph.Defaults.BudgetTimeLimitSeconds.HasValue) builder.AppendLine($"    timeLimitSeconds: {graph.Defaults.BudgetTimeLimitSeconds.Value}");
            }
        }

        builder.AppendLine("nodes:");
        foreach (var node in graph.Nodes)
        {
            AppendNode(builder, node, indent: 2);
        }

        builder.AppendLine("edges:");
        foreach (var edge in graph.Edges)
        {
            AppendEdge(builder, edge, indent: 2);
        }

        if (graph.Subgraphs.Count > 0)
        {
            builder.AppendLine("subgraphs:");
            foreach (var pair in graph.Subgraphs)
            {
                builder.Append(' ', 2).Append(pair.Key).AppendLine(":");
                if (pair.Value.Nodes.Count > 0)
                {
                    builder.Append(' ', 4).AppendLine("nodes:");
                    foreach (var node in pair.Value.Nodes)
                    {
                        AppendNode(builder, node, indent: 6);
                    }
                }
                if (pair.Value.Edges.Count > 0)
                {
                    builder.Append(' ', 4).AppendLine("edges:");
                    foreach (var edge in pair.Value.Edges)
                    {
                        AppendEdge(builder, edge, indent: 6);
                    }
                }
            }
        }

        if (graph.Layout.Count > 0)
        {
            builder.AppendLine("layout:");
            foreach (var pair in graph.Layout)
            {
                builder.AppendLine($"  {pair.Key}: {{ x: {Number(pair.Value.X)}, y: {Number(pair.Value.Y)} }}");
            }
        }
        return builder.ToString();
    }

    /// <summary><paramref name="indent"/> はリスト項目の "- " が始まる桁。</summary>
    private static void AppendNode(StringBuilder builder, GraphNode node, int indent)
    {
        var inner = indent + 2;
        builder.Append(' ', indent).Append("- id: ").AppendLine(Quote(node.Id));
        builder.Append(' ', inner).Append("kind: ").AppendLine(node.Kind.ToString().ToLowerInvariant());
        Append(builder, "agent", node.Agent, inner);
        Append(builder, "team", node.Team, inner);
        Append(builder, "input", node.Input, inner);
        Append(builder, "goal", node.Goal, inner);
        Append(builder, "body", node.Body, inner);
        Append(builder, "title", node.Title, inner);
        Append(builder, "summary", node.Summary, inner);
        if (node.TimeoutSeconds.HasValue) builder.Append(' ', inner).Append("timeoutSeconds: ").AppendLine(node.TimeoutSeconds.Value.ToString(CultureInfo.InvariantCulture));
        Append(builder, "codeFile", node.CodeFile, inner);
        Append(builder, "graph", node.Graph, inner);
        if (node.Next.Count > 0) builder.Append(' ', inner).Append("next: [").Append(string.Join(", ", node.Next.Select(Quote))).AppendLine("]");

        if (node.Stop is not null)
        {
            builder.Append(' ', inner).AppendLine("stop:");
            builder.Append(' ', inner + 2).Append("maxIterations: ").AppendLine(node.Stop.MaxIterations.ToString(CultureInfo.InvariantCulture));
            if (node.Stop.CostLimitUsd.HasValue) builder.Append(' ', inner + 2).Append("costLimitUsd: ").AppendLine(Number(node.Stop.CostLimitUsd.Value));
            if (node.Stop.TimeLimitSeconds.HasValue) builder.Append(' ', inner + 2).Append("timeLimitSeconds: ").AppendLine(node.Stop.TimeLimitSeconds.Value.ToString(CultureInfo.InvariantCulture));
            if (node.Stop.ScoreThreshold.HasValue) builder.Append(' ', inner + 2).Append("scoreThreshold: ").AppendLine(Number(node.Stop.ScoreThreshold.Value));
        }

        if (node.Evaluator is not null)
        {
            builder.Append(' ', inner).AppendLine("evaluator:");
            builder.Append(' ', inner + 2).Append("kind: ").AppendLine(Quote(node.Evaluator.Kind));
            Append(builder, "node", node.Evaluator.Node, inner + 2);
            Append(builder, "agent", node.Evaluator.Agent, inner + 2);
            if (node.Evaluator.Metrics.Count > 0)
            {
                builder.Append(' ', inner + 2).AppendLine("metrics:");
                foreach (var metric in node.Evaluator.Metrics)
                {
                    builder.Append(' ', inner + 4).Append("- name: ").AppendLine(Quote(metric.Name));
                    builder.Append(' ', inner + 6).Append("target: ").AppendLine(Number(metric.Target));
                    if (!string.IsNullOrWhiteSpace(metric.Direction) && !string.Equals(metric.Direction, "gte", StringComparison.Ordinal))
                    {
                        builder.Append(' ', inner + 6).Append("direction: ").AppendLine(Quote(metric.Direction));
                    }
                }
            }
        }

        if (node.JoinPolicy.HasValue) builder.Append(' ', inner).Append("joinPolicy: ").AppendLine(node.JoinPolicy.Value.ToString().ToLowerInvariant());
        if (node.OnPartialFailure.HasValue) builder.Append(' ', inner).Append("onPartialFailure: ").AppendLine(node.OnPartialFailure.Value.ToString().ToLowerInvariant());
        Append(builder, "alternate", node.Alternate, inner);
    }

    private static void AppendEdge(StringBuilder builder, GraphEdge edge, int indent)
    {
        var inner = indent + 2;
        builder.Append(' ', indent).Append("- id: ").AppendLine(Quote(edge.Id));
        builder.Append(' ', inner).Append("from: ").AppendLine(Quote(edge.From));
        builder.Append(' ', inner).Append("to: ").AppendLine(Quote(edge.To));
        Append(builder, "condition", edge.Condition, inner);
        if (edge.LoopBack) builder.Append(' ', inner).AppendLine("loopBack: true");
    }

    private static void Append(StringBuilder builder, string name, string? value, int indent)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            builder.Append(' ', indent).Append(name).Append(": ").AppendLine(Quote(value));
        }
    }

    private static string Number(double value) => value.ToString("R", CultureInfo.InvariantCulture);

    /// <summary>
    /// YAML の二重引用符スタイルで出力する。改行やタブはエスケープしないとインデントが崩れるため、
    /// 制御文字も含めて明示的に置き換える。
    /// </summary>
    internal static string Quote(string value)
    {
        var builder = new StringBuilder(value.Length + 2);
        builder.Append('"');
        foreach (var character in value)
        {
            switch (character)
            {
                case '\\': builder.Append("\\\\"); break;
                case '"': builder.Append("\\\""); break;
                case '\n': builder.Append("\\n"); break;
                case '\r': builder.Append("\\r"); break;
                case '\t': builder.Append("\\t"); break;
                default: builder.Append(character); break;
            }
        }
        builder.Append('"');
        return builder.ToString();
    }
}
