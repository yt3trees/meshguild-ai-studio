using Microsoft.Extensions.Logging.Abstractions;
using WorkAgents.Agents.Tools;

namespace WorkAgents.UnitTests;

public sealed class ScriptToolLoaderTests
{
    [Fact]
    public void Load_ValidManifest_ReturnsProvider()
    {
        var dir = CreateTempDir();
        try
        {
            WriteManifest(dir, "send_slack", runtime: "node", entryPointContent: "// no-op");

            var (providers, preFailures) = ScriptToolLoader.Load([dir], new ToolPluginHostAllowlist([]), NullLogger.Instance);

            var provider = Assert.Single(providers);
            Assert.Equal("sales-report-agent", provider.Provider.AgentName);
            Assert.Empty(preFailures);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Load_InvalidManifest_IsSkippedAndRecordedAsFailed()
    {
        var dir = CreateTempDir();
        try
        {
            File.WriteAllText(Path.Combine(dir, "bad.tool.yaml"), "name: only-name-field");

            var (providers, preFailures) = ScriptToolLoader.Load([dir], new ToolPluginHostAllowlist([]), NullLogger.Instance);

            Assert.Empty(providers);
            var failure = Assert.Single(preFailures);
            Assert.Equal(ToolPluginLoadStatus.Failed, failure.LoadStatus);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Load_MissingEntryPoint_IsSkippedAndRecordedAsFailed()
    {
        var dir = CreateTempDir();
        try
        {
            File.WriteAllText(Path.Combine(dir, "missing_script.tool.yaml"), """
                name: missing_script
                description: desc
                agentName: sales-report-agent
                runtime: node
                entryPoint: does_not_exist.js
                approval: automatic
                """);

            var (providers, preFailures) = ScriptToolLoader.Load([dir], new ToolPluginHostAllowlist([]), NullLogger.Instance);

            Assert.Empty(providers);
            var failure = Assert.Single(preFailures);
            Assert.Equal(ToolPluginLoadStatus.Failed, failure.LoadStatus);
            Assert.Contains("entryPoint", failure.FailureReason, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Load_AllowedHostsNotInGlobalAllowlist_IsSkippedAndRecordedAsFailed()
    {
        var dir = CreateTempDir();
        try
        {
            WriteManifest(dir, "send_slack", runtime: "node", entryPointContent: "// no-op", allowedHosts: ["hooks.slack.com"]);

            var (providers, preFailures) = ScriptToolLoader.Load([dir], new ToolPluginHostAllowlist(["other-host.example.com"]), NullLogger.Instance);

            Assert.Empty(providers);
            var failure = Assert.Single(preFailures);
            Assert.Equal(ToolPluginLoadStatus.Failed, failure.LoadStatus);
            Assert.Contains("hooks.slack.com", failure.FailureReason, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Load_AllowedHostsInGlobalAllowlist_Succeeds()
    {
        var dir = CreateTempDir();
        try
        {
            WriteManifest(dir, "send_slack", runtime: "node", entryPointContent: "// no-op", allowedHosts: ["hooks.slack.com"]);

            var (providers, preFailures) = ScriptToolLoader.Load([dir], new ToolPluginHostAllowlist(["hooks.slack.com"]), NullLogger.Instance);

            Assert.Single(providers);
            Assert.Empty(preFailures);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Load_MultipleManifestsInSameDirectory_AllLoadIndependently()
    {
        var dir = CreateTempDir();
        try
        {
            WriteManifest(dir, "tool_a", runtime: "node", entryPointContent: "// a");
            WriteManifest(dir, "tool_b", runtime: "python", entryPointContent: "# b");

            var (providers, preFailures) = ScriptToolLoader.Load([dir], new ToolPluginHostAllowlist([]), NullLogger.Instance);

            Assert.Equal(2, providers.Count);
            Assert.Empty(preFailures);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    private static string CreateTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "work-agents-tests", $"script-tools-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void WriteManifest(
        string dir,
        string name,
        string runtime,
        string entryPointContent,
        IReadOnlyList<string>? allowedHosts = null)
    {
        var entryPoint = runtime == "node" ? $"{name}.js" : $"{name}.py";
        File.WriteAllText(Path.Combine(dir, entryPoint), entryPointContent);

        var allowedHostsYaml = allowedHosts is { Count: > 0 }
            ? "allowedHosts:\n" + string.Join("\n", allowedHosts.Select(h => $"  - {h}"))
            : "";

        File.WriteAllText(Path.Combine(dir, $"{name}.tool.yaml"), $"""
            name: {name}
            description: {name} description
            agentName: sales-report-agent
            runtime: {runtime}
            entryPoint: {entryPoint}
            approval: automatic
            {allowedHostsYaml}
            """);
    }
}
