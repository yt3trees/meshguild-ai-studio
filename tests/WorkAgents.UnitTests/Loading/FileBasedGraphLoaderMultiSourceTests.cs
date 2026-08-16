using Microsoft.Extensions.Logging.Abstractions;
using WorkAgents.Agents.Configuration;
using WorkAgents.Agents.Loading;

namespace WorkAgents.UnitTests.Loading;

public sealed class FileBasedGraphLoaderMultiSourceTests
{
    [Fact]
    public void LoadAllFromSources_MergesAndOverridesAcrossSources()
    {
        var root = Path.Combine(Path.GetTempPath(), "work-agents-tests", $"{Guid.NewGuid():N}");
        try
        {
            var standard = Path.Combine(root, "standard");
            WriteGraph(standard, "demo-graph", "Standard demo graph");
            WriteGraph(standard, "other-graph", "Other graph");

            var team = Path.Combine(root, "team-sales");
            WriteGraph(team, "demo-graph", "Team demo graph (customized)");

            var loader = new FileBasedGraphLoader(NullLogger<FileBasedGraphLoader>.Instance);
            var sources = new[]
            {
                new DefinitionSourceEntry { Label = "standard", Path = standard },
                new DefinitionSourceEntry { Label = "team-sales", Path = team },
            };

            var defs = loader.LoadAllFromSources(sources);

            Assert.Equal(2, defs.Count);
            var demoGraph = Assert.Single(defs, d => d.Name == "demo-graph");
            Assert.Equal("team-sales", demoGraph.SourceLabel);
            Assert.Equal(new[] { "standard" }, demoGraph.OverriddenSourceLabels);
            Assert.Equal("Team demo graph (customized)", demoGraph.Description);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void LoadAllFromSources_InvalidGraphIsSkippedNotThrown()
    {
        var root = Path.Combine(Path.GetTempPath(), "work-agents-tests", $"{Guid.NewGuid():N}");
        try
        {
            var standard = Path.Combine(root, "standard");
            WriteGraph(standard, "good-graph", "Good graph");
            var badDir = Path.Combine(standard, "graphs", "bad-graph");
            Directory.CreateDirectory(badDir);
            File.WriteAllText(Path.Combine(badDir, "graph.yaml"), "unknownTopLevelKey: true");

            var loader = new FileBasedGraphLoader(NullLogger<FileBasedGraphLoader>.Instance);
            var sources = new[] { new DefinitionSourceEntry { Label = "standard", Path = standard } };

            var defs = loader.LoadAllFromSources(sources);

            var good = Assert.Single(defs);
            Assert.Equal("good-graph", good.Name);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static void WriteGraph(string sourceRoot, string name, string description)
    {
        var dir = Path.Combine(sourceRoot, "graphs", name);
        Directory.CreateDirectory(dir);
        var yaml = $"""
            version: 1
            name: {name}
            description: {description}
            nodes:
              - id: start
                kind: agent
                agent: repo-agent
                input: "REPLACED_INPUT"
            edges: []
            """.Replace("REPLACED_INPUT", "${mission.goal}");
        File.WriteAllText(Path.Combine(dir, "graph.yaml"), yaml);
    }
}
