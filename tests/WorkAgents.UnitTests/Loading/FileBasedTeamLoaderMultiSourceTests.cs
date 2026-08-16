using Microsoft.Extensions.Logging.Abstractions;
using WorkAgents.Agents.Configuration;
using WorkAgents.Agents.Loading;

namespace WorkAgents.UnitTests.Loading;

public sealed class FileBasedTeamLoaderMultiSourceTests
{
    private static readonly string[] KnownAgents = { "orchestrator-agent", "dev-agent", "test-agent" };

    [Fact]
    public void LoadAllFromSources_MergesAndOverridesAcrossSources()
    {
        var root = Path.Combine(Path.GetTempPath(), "work-agents-tests", $"{Guid.NewGuid():N}");
        try
        {
            var standard = Path.Combine(root, "standard");
            WriteTeam(standard, "demo-team", "dev-agent");
            WriteTeam(standard, "other-team", "test-agent");

            var team = Path.Combine(root, "team-sales");
            WriteTeam(team, "demo-team", "dev-agent", "test-agent");

            var loader = new FileBasedTeamLoader(NullLogger<FileBasedTeamLoader>.Instance);
            var sources = new[]
            {
                new DefinitionSourceEntry { Label = "standard", Path = standard },
                new DefinitionSourceEntry { Label = "team-sales", Path = team },
            };

            var defs = loader.LoadAllFromSources(sources, KnownAgents);

            Assert.Equal(2, defs.Count);
            var demoTeam = Assert.Single(defs, d => d.Name == "demo-team");
            Assert.Equal("team-sales", demoTeam.SourceLabel);
            Assert.Equal(new[] { "standard" }, demoTeam.OverriddenSourceLabels);
            Assert.Equal(2, demoTeam.Members.Count);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void LoadAllFromSources_InvalidTeamIsSkippedNotThrown()
    {
        var root = Path.Combine(Path.GetTempPath(), "work-agents-tests", $"{Guid.NewGuid():N}");
        try
        {
            var standard = Path.Combine(root, "standard");
            WriteTeam(standard, "good-team", "dev-agent");
            var badDir = Path.Combine(standard, "teams", "bad-team");
            Directory.CreateDirectory(badDir);
            File.WriteAllText(Path.Combine(badDir, "team.yaml"), """
                version: 1
                name: bad-team
                orchestrator:
                  agent: unknown-agent
                members:
                  - agent: dev-agent
                """);

            var loader = new FileBasedTeamLoader(NullLogger<FileBasedTeamLoader>.Instance);
            var sources = new[] { new DefinitionSourceEntry { Label = "standard", Path = standard } };

            var defs = loader.LoadAllFromSources(sources, KnownAgents);

            var good = Assert.Single(defs);
            Assert.Equal("good-team", good.Name);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static void WriteTeam(string sourceRoot, string name, params string[] members)
    {
        var dir = Path.Combine(sourceRoot, "teams", name);
        Directory.CreateDirectory(dir);
        var membersYaml = string.Join("\n", members.Select(m => $"  - agent: {m}"));
        File.WriteAllText(Path.Combine(dir, "team.yaml"), $"""
            version: 1
            name: {name}
            orchestrator:
              agent: orchestrator-agent
            members:
            {membersYaml}
            """);
    }
}
