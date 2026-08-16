using System.ComponentModel;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using WorkAgents.Agents;
using WorkAgents.Core.Graphs;
using WorkAgents.Core.Teams;

namespace WorkAgents.Host.Mcp;

public sealed record McpDefinitionSummary(
    string Kind,
    string Name,
    string? DisplayName,
    string? Description,
    string SourceLabel);

public static class McpDefinitionProjector
{
    public static IReadOnlyList<McpDefinitionSummary> ProjectAgents(IEnumerable<AgentView> agents)
        => agents
            .Select(agent => new McpDefinitionSummary(
                "agent",
                agent.Name,
                McpResponseProjector.SafeText(agent.DisplayName, 200),
                McpResponseProjector.SafeText(agent.Description),
                McpRedaction.SafeName(agent.SourceLabel, "standard")))
            .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    public static IReadOnlyList<McpDefinitionSummary> ProjectTeams(IEnumerable<TeamDefinition> teams)
        => teams
            .Select(team => new McpDefinitionSummary(
                "team",
                team.Name,
                McpResponseProjector.SafeText(team.DisplayName, 200),
                McpResponseProjector.SafeText(team.Description),
                McpRedaction.SafeName(team.SourceLabel, "standard")))
            .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    public static IReadOnlyList<McpDefinitionSummary> ProjectGraphs(IEnumerable<GraphDefinition> graphs)
        => graphs
            .Select(graph => new McpDefinitionSummary(
                "graph",
                graph.Name,
                McpResponseProjector.SafeText(graph.DisplayName, 200),
                McpResponseProjector.SafeText(graph.Description),
                McpRedaction.SafeName(graph.SourceLabel, "standard")))
            .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
}

[McpServerToolType]
public sealed class McpDefinitionTools
{
    private readonly IAgentRegistry _agents;
    private readonly IReadOnlyList<TeamDefinition> _teams;
    private readonly IReadOnlyList<GraphDefinition> _graphs;
    private readonly McpRequestValidator _validator;

    public McpDefinitionTools(
        IAgentRegistry agents,
        IReadOnlyList<TeamDefinition> teams,
        IReadOnlyList<GraphDefinition> graphs,
        McpRequestValidator validator)
    {
        _agents = agents;
        _teams = teams;
        _graphs = graphs;
        _validator = validator;
    }

    [McpServerTool, Description("List safe summaries of the WorkAgents agents, teams, and graphs available to this server.")]
    public McpDefinitionListResult workagents_list_definitions(
        [Description("Definition kind: all, agent, team, or graph.")] string kind = "all",
        [Description("Numeric offset cursor returned by a previous page.")] int offset = 0)
    {
        var definitions = kind.Trim().ToLowerInvariant() switch
        {
            "all" => McpDefinitionProjector.ProjectAgents(_agents.ListAgents())
                .Concat(McpDefinitionProjector.ProjectTeams(_teams))
                .Concat(McpDefinitionProjector.ProjectGraphs(_graphs))
                .OrderBy(item => item.Kind, StringComparer.Ordinal)
                .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            "agent" => McpDefinitionProjector.ProjectAgents(_agents.ListAgents()),
            "team" => McpDefinitionProjector.ProjectTeams(_teams),
            "graph" => McpDefinitionProjector.ProjectGraphs(_graphs),
            _ => throw new McpException("[invalid_input] kind must be all, agent, team, or graph."),
        };

        var page = McpResponseProjector.Page(definitions, Math.Max(0, offset), _validator.ClampPageSize(null), out var nextOffset);
        return new McpDefinitionListResult(page, nextOffset);
    }
}

public sealed record McpDefinitionListResult(
    IReadOnlyList<McpDefinitionSummary> Definitions,
    int? NextOffset);
