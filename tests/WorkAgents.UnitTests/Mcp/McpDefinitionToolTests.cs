using WorkAgents.Agents;
using WorkAgents.Host.Mcp;

namespace WorkAgents.UnitTests.Mcp;

public sealed class McpDefinitionToolTests
{
    [Fact]
    public void ProjectAgents_ReturnsSafeDeterministicSummaries()
    {
        var result = McpDefinitionProjector.ProjectAgents([
            new AgentView("z-agent", "Z Agent", "second", [], "team-sales"),
            new AgentView("a-agent", "A Agent", "first", [], "standard"),
        ]);

        Assert.Collection(
            result,
            first =>
            {
                Assert.Equal("agent", first.Kind);
                Assert.Equal("a-agent", first.Name);
                Assert.Equal("standard", first.SourceLabel);
            },
            second => Assert.Equal("z-agent", second.Name));
    }

    [Fact]
    public void ProjectAgents_DoesNotExposeAttachedSkillContent()
    {
        var result = McpDefinitionProjector.ProjectAgents([
            new AgentView("agent", "Agent", "Description", [new SkillView("secret", "shared", "secret body")]),
        ]);

        var summary = Assert.Single(result);
        Assert.DoesNotContain("secret body", summary.Description ?? "", StringComparison.Ordinal);
        Assert.Equal("Description", summary.Description);
    }
}
