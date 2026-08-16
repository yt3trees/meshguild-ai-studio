using Microsoft.Extensions.Logging.Abstractions;
using WorkAgents.Agents.Configuration;
using WorkAgents.Agents.Loading;

namespace WorkAgents.UnitTests.Loading;

public sealed class FileBasedWorkflowLoaderMultiSourceTests
{
    [Fact]
    public void LoadFromSources_MergesAndOverridesAcrossSources()
    {
        var root = Path.Combine(Path.GetTempPath(), "work-agents-tests", $"{Guid.NewGuid():N}");
        try
        {
            var standard = Path.Combine(root, "standard");
            WriteWorkflow(standard, "demo-workflow", "meeting-agent");
            WriteWorkflow(standard, "other-workflow", "meeting-agent");

            var team = Path.Combine(root, "team-sales");
            WriteWorkflow(team, "demo-workflow", "repo-agent");

            var loader = new FileBasedWorkflowLoader(Path.Combine(standard, "workflows"), NullLogger<FileBasedWorkflowLoader>.Instance);
            var sources = new[]
            {
                new DefinitionSourceEntry { Label = "standard", Path = standard },
                new DefinitionSourceEntry { Label = "team-sales", Path = team },
            };

            var defs = loader.LoadFromSources(sources);

            Assert.Equal(2, defs.Count);
            var demoWorkflow = Assert.Single(defs, d => d.Name == "demo-workflow");
            Assert.Equal("team-sales", demoWorkflow.SourceLabel);
            Assert.Equal(new[] { "standard" }, demoWorkflow.OverriddenSourceLabels);
            Assert.Equal("repo-agent", demoWorkflow.Steps[0].Agent);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static void WriteWorkflow(string sourceRoot, string name, string agent)
    {
        var dir = Path.Combine(sourceRoot, "workflows", name);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "workflow.yaml"), $"""
            kind: Workflow
            name: {name}
            steps:
              - name: first
                agent: {agent}
                input: hello
            """);
    }
}
