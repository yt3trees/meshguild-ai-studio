using Microsoft.Extensions.Logging.Abstractions;
using WorkAgents.Agents.Loading;

namespace WorkAgents.UnitTests;

public sealed class FileBasedWorkflowLoaderTests
{
    [Fact]
    public void LoadsWorkflowWithStepsAndCron()
    {
        var root = Path.Combine(Path.GetTempPath(), "work-agents-tests", $"{Guid.NewGuid():N}");
        var dir = Path.Combine(root, "demo-workflow");
        Directory.CreateDirectory(dir);
        try
        {
            var yaml = """
                kind: Workflow
                name: demo-workflow
                displayName: Demo
                description: demo description
                schedule:
                  cron: "0 10 * * *"
                steps:
                  - name: first
                    agent: meeting-agent
                    input: |
                      Hello ${workflow.input}
                  - name: second
                    agent: meeting-agent
                    input: |
                      From: ${steps.first.result}
                """;
            File.WriteAllText(Path.Combine(dir, "workflow.yaml"), yaml);

            var loader = new FileBasedWorkflowLoader(root, NullLogger<FileBasedWorkflowLoader>.Instance);
            var defs = loader.Load();

            Assert.Single(defs);
            var d = defs[0];
            Assert.Equal("demo-workflow", d.Name);
            Assert.Equal("Demo", d.DisplayName);
            Assert.Equal("0 10 * * *", d.ScheduleCron);
            Assert.Equal(2, d.Steps.Count);
            Assert.Equal("first", d.Steps[0].Name);
            Assert.Equal("meeting-agent", d.Steps[0].Agent);
            Assert.Contains("${steps.first.result}", d.Steps[1].Input);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void MissingRootReturnsEmpty()
    {
        var loader = new FileBasedWorkflowLoader(Path.Combine(Path.GetTempPath(), "work-agents-tests", $"does-not-exist-{Guid.NewGuid():N}"),
            NullLogger<FileBasedWorkflowLoader>.Instance);
        Assert.Empty(loader.Load());
    }

    [Fact]
    public void FolderNameUsedWhenNameMissing()
    {
        var root = Path.Combine(Path.GetTempPath(), "work-agents-tests", $"{Guid.NewGuid():N}");
        var dir = Path.Combine(root, "implicit-name");
        Directory.CreateDirectory(dir);
        try
        {
            var yaml = """
                kind: Workflow
                steps:
                  - name: only
                    agent: meeting-agent
                    input: hello
                """;
            File.WriteAllText(Path.Combine(dir, "workflow.yaml"), yaml);

            var loader = new FileBasedWorkflowLoader(root, NullLogger<FileBasedWorkflowLoader>.Instance);
            var defs = loader.Load();
            Assert.Single(defs);
            Assert.Equal("implicit-name", defs[0].Name);
            Assert.Null(defs[0].ScheduleCron);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}