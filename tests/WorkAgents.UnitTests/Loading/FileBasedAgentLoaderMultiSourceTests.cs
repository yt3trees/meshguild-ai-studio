using Microsoft.Extensions.Logging.Abstractions;
using WorkAgents.Agents.Configuration;
using WorkAgents.Agents.Loading;

namespace WorkAgents.UnitTests.Loading;

public sealed class FileBasedAgentLoaderMultiSourceTests
{
    [Fact]
    public void LoadFromSources_MergesAndOverridesAcrossSources()
    {
        var root = Path.Combine(Path.GetTempPath(), "work-agents-tests", $"{Guid.NewGuid():N}");
        try
        {
            var standard = CreateSourceRoot(root, "standard");
            WriteAgent(standard, "dev-agent", "Standard dev agent");
            WriteAgent(standard, "meeting-agent", "Standard meeting agent");

            var team = CreateSourceRoot(root, "team-sales");
            WriteAgent(team, "dev-agent", "Team dev agent (customized)");

            var loader = new FileBasedAgentLoader(Path.Combine(standard, "agents"), NullLogger<FileBasedAgentLoader>.Instance);
            var sources = new[]
            {
                new DefinitionSourceEntry { Label = "standard", Path = standard },
                new DefinitionSourceEntry { Label = "team-sales", Path = team },
            };

            var defs = loader.LoadFromSources(sources);

            Assert.Equal(2, defs.Count);
            var devAgent = Assert.Single(defs, d => d.Name == "dev-agent");
            Assert.Equal("team-sales", devAgent.SourceLabel);
            Assert.Equal(new[] { "standard" }, devAgent.OverriddenSourceLabels);
            Assert.Equal("Team dev agent (customized)", devAgent.Description);

            var meetingAgent = Assert.Single(defs, d => d.Name == "meeting-agent");
            Assert.Equal("standard", meetingAgent.SourceLabel);
            Assert.Empty(meetingAgent.OverriddenSourceLabels);
            Assert.Equal("Prompt", meetingAgent.Kind);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void LoadFromSources_ResolvesSharedSkillsFromAdditionalSource()
    {
        var root = Path.Combine(Path.GetTempPath(), "work-agents-tests", $"{Guid.NewGuid():N}");
        try
        {
            var standard = CreateSourceRoot(root, "standard");
            WriteAgent(standard, "dev-agent", "Standard dev agent");

            var team = CreateSourceRoot(root, "team-sales");
            WriteSkill(team, "team-review", "Team review skill");
            WriteAgent(team, "sample-agent", "Team sample agent", "team-review");

            var loader = new FileBasedAgentLoader(Path.Combine(standard, "agents"), NullLogger<FileBasedAgentLoader>.Instance);
            var sources = new[]
            {
                new DefinitionSourceEntry { Label = "standard", Path = standard },
                new DefinitionSourceEntry { Label = "team-sales", Path = team },
            };

            var definition = Assert.Single(loader.LoadFromSources(sources), agent => agent.Name == "sample-agent");

            Assert.Equal(["team-review"], definition.SharedSkillNames);
            Assert.Equal(
                Path.GetFullPath(Path.Combine(team, "skills", "team-review")),
                definition.SharedSkillPaths["team-review"]);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string CreateSourceRoot(string root, string label)
    {
        var path = Path.Combine(root, label);
        Directory.CreateDirectory(Path.Combine(path, "agents"));
        return path;
    }

    private static void WriteAgent(string sourceRoot, string name, string description, string? skillName = null)
    {
        var dir = Path.Combine(sourceRoot, "agents", name);
        Directory.CreateDirectory(dir);
        var skills = skillName is null ? "" : $"\nskills:\n  - {skillName}";
        File.WriteAllText(Path.Combine(dir, "agent.yaml"), $"""
            kind: Prompt
            name: {name}
            description: {description}
            {skills}
            """);
        File.WriteAllText(Path.Combine(dir, "instructions.md"), $"# {name}");
    }

    private static void WriteSkill(string sourceRoot, string name, string description)
    {
        var dir = Path.Combine(sourceRoot, "skills", name);
        Directory.CreateDirectory(dir);
        File.WriteAllText(
            Path.Combine(dir, "SKILL.md"),
            $"""
            ---
            name: {name}
            description: {description}
            ---
            # {name}
            """);
    }
}
