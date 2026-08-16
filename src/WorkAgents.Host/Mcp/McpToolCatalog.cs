namespace WorkAgents.Host.Mcp;

public static class McpToolCatalog
{
    public static IReadOnlySet<string> CoreToolNames { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "workagents_list_definitions",
        "workagents_submit_mission",
        "workagents_get_mission",
        "workagents_get_graph",
        "workagents_list_artifacts",
        "workagents_get_approval",
        "workagents_cancel_mission",
    };

    public static bool IsAllowed(string name) => CoreToolNames.Contains(name);

    public static IReadOnlyList<string> Sort(IEnumerable<string> names)
        => names.OrderBy(name => name, StringComparer.Ordinal).ToArray();
}
