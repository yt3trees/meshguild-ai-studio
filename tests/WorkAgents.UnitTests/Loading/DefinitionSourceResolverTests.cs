using Microsoft.Extensions.Logging.Abstractions;
using WorkAgents.Agents.Configuration;
using WorkAgents.Agents.Loading;

namespace WorkAgents.UnitTests.Loading;

public sealed class DefinitionSourceResolverTests
{
    [Fact]
    public void ResolveFolders_MergesAcrossSources()
    {
        var root = CreateTempRoot();
        try
        {
            var standard = CreateSource(root, "standard", "agents", "agent-a", "agent-b");
            var team = CreateSource(root, "team-sales", "agents", "agent-c");

            var resolver = new DefinitionSourceResolver([standard, team], NullLogger<DefinitionSourceResolver>.Instance);
            var (folders, summary) = resolver.ResolveFolders("agents");

            Assert.Equal(3, folders.Count);
            Assert.Equal(0, summary.OverrideCount);
            Assert.Empty(summary.SkippedSourceLabels);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ResolveFolders_LaterSourceOverridesEarlier()
    {
        var root = CreateTempRoot();
        try
        {
            var standard = CreateSource(root, "standard", "agents", "dev-agent");
            var team = CreateSource(root, "team-sales", "agents", "dev-agent");

            var resolver = new DefinitionSourceResolver([standard, team], NullLogger<DefinitionSourceResolver>.Instance);
            var (folders, summary) = resolver.ResolveFolders("agents");

            var resolved = Assert.Single(folders);
            Assert.Equal("team-sales", resolved.SourceLabel);
            Assert.Equal(new[] { "standard" }, resolved.OverriddenSourceLabels);
            Assert.Equal(1, summary.OverrideCount);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ResolveFolders_SkipsMissingSourcePathAndContinues()
    {
        var root = CreateTempRoot();
        try
        {
            var standard = CreateSource(root, "standard", "agents", "dev-agent");
            var missing = new DefinitionSourceEntry { Label = "team-missing", Path = Path.Combine(root, "does-not-exist") };

            var resolver = new DefinitionSourceResolver([standard, missing], NullLogger<DefinitionSourceResolver>.Instance);
            var (folders, summary) = resolver.ResolveFolders("agents");

            var resolved = Assert.Single(folders);
            Assert.Equal("dev-agent", resolved.Name);
            Assert.Contains("team-missing", summary.SkippedSourceLabels);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Constructor_DuplicateLabel_Throws()
    {
        var root = CreateTempRoot();
        try
        {
            var a = new DefinitionSourceEntry { Label = "standard", Path = root };
            var b = new DefinitionSourceEntry { Label = "standard", Path = root };
            Assert.Throws<ArgumentException>(() => new DefinitionSourceResolver([a, b]));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string CreateTempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "work-agents-tests", $"{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        return root;
    }

    private static DefinitionSourceEntry CreateSource(string root, string label, string subFolder, params string[] definitionNames)
    {
        var sourcePath = Path.Combine(root, label);
        foreach (var name in definitionNames)
        {
            Directory.CreateDirectory(Path.Combine(sourcePath, subFolder, name));
        }

        return new DefinitionSourceEntry { Label = label, Path = sourcePath };
    }
}
