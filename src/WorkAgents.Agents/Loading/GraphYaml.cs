using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;
using WorkAgents.Core.Graphs;

namespace WorkAgents.Agents.Loading;

public sealed class GraphYaml
{
    public int? Version { get; set; }
    public string? Name { get; set; }
    public string? DisplayName { get; set; }
    public string? Description { get; set; }
    public GraphDefaultsYaml? Defaults { get; set; }
    public List<GraphNodeYaml>? Nodes { get; set; }
    public List<GraphEdgeYaml>? Edges { get; set; }
    public Dictionary<string, GraphSubgraphYaml>? Subgraphs { get; set; }
    public Dictionary<string, GraphLayoutYaml>? Layout { get; set; }
}

public sealed class GraphDefaultsYaml
{
    public string? Team { get; set; }
    public GraphBudgetYaml? Budget { get; set; }
}

public sealed class GraphBudgetYaml
{
    public double? CostLimitUsd { get; set; }
    public int? TimeLimitSeconds { get; set; }
}

public sealed class GraphNodeYaml
{
    public string? Id { get; set; }
    public string? Kind { get; set; }
    public string? Agent { get; set; }
    public string? Team { get; set; }
    public string? Input { get; set; }
    public string? Goal { get; set; }
    public string? Body { get; set; }
    public GraphStopYaml? Stop { get; set; }
    public GraphEvaluatorYaml? Evaluator { get; set; }
    public string? Title { get; set; }
    public string? Summary { get; set; }
    public int? TimeoutSeconds { get; set; }
    public string? JoinPolicy { get; set; }
    public string? OnPartialFailure { get; set; }
    public string? Alternate { get; set; }
    public string? CodeFile { get; set; }
    public string? Graph { get; set; }
    public List<string>? Next { get; set; }
}

public sealed class GraphStopYaml
{
    public int? MaxIterations { get; set; }
    public double? CostLimitUsd { get; set; }
    public int? TimeLimitSeconds { get; set; }
    public double? ScoreThreshold { get; set; }
}

public sealed class GraphEvaluatorYaml
{
    public string? Kind { get; set; }
    public string? Node { get; set; }
    public string? Agent { get; set; }
    public List<GraphMetricYaml>? Metrics { get; set; }
}

public sealed class GraphMetricYaml
{
    public string? Name { get; set; }
    public double? Target { get; set; }
    public string? Direction { get; set; }
}

public sealed class GraphEdgeYaml
{
    public string? Id { get; set; }
    public string? From { get; set; }
    public string? To { get; set; }
    public string? Condition { get; set; }
    public bool LoopBack { get; set; }
}

public sealed class GraphSubgraphYaml
{
    public List<GraphNodeYaml>? Nodes { get; set; }
    public List<GraphEdgeYaml>? Edges { get; set; }
}

public sealed class GraphLayoutYaml
{
    public double X { get; set; }
    public double Y { get; set; }
}

public sealed class GraphYamlValidationException : Exception
{
    public GraphYamlValidationException(string message) : base(message)
    {
    }
}

internal static class GraphYamlSerializer
{
    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .Build();

    public static GraphYaml Deserialize(string yaml)
    {
        try
        {
            return Deserializer.Deserialize<GraphYaml>(yaml) ?? new GraphYaml();
        }
        catch (YamlDotNet.Core.YamlException ex)
        {
            throw new GraphYamlValidationException($"unknown key: {ex.Message}");
        }
    }
}
