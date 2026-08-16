using WorkAgents.Agents.Loading;

namespace WorkAgents.UnitTests.Loading;

public sealed class DefinitionRootResolverTests
{
    [Fact]
    public void ResolveStandardSourceRoot_PublishedLayout_UsesSiblingDefinitions()
    {
        var root = Path.Combine(Path.GetTempPath(), "WorkAgentsDefinitionRoot_" + Guid.NewGuid().ToString("N"));
        var definitionsRoot = Path.Combine(root, "definitions");
        Directory.CreateDirectory(Path.Combine(definitionsRoot, "skills"));
        Directory.CreateDirectory(Path.Combine(definitionsRoot, "agents", "skill-agent"));
        File.WriteAllText(
            Path.Combine(definitionsRoot, "agents", "skill-agent", "agent.yaml"),
            "kind: Prompt\nname: skill-agent\ndescription: Uses a shared skill.\nskills:\n  - shared\n");
        File.WriteAllText(
            Path.Combine(definitionsRoot, "agents", "skill-agent", "instructions.md"),
            "# Skill agent");
        Directory.CreateDirectory(Path.Combine(definitionsRoot, "skills", "shared"));
        File.WriteAllText(
            Path.Combine(definitionsRoot, "skills", "shared", "SKILL.md"),
            "---\nname: shared\ndescription: Shared test skill\n---\n");

        try
        {
            var processDirectory = Path.Combine(root, "WorkAgents.Host");

            Assert.Equal(
                Path.GetFullPath(Path.Combine(root, "definitions")),
                FileBasedAgentLoader.ResolveStandardSourceRoot(processDirectory));
            Assert.Equal(
                Path.GetFullPath(Path.Combine(root, "definitions", "agents")),
                FileBasedAgentLoader.ResolveAgentsRoot(processDirectory));

            var definition = Assert.Single(
                new FileBasedAgentLoader(FileBasedAgentLoader.ResolveAgentsRoot(processDirectory)).Load());
            Assert.Equal(["shared"], definition.SharedSkillNames);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ResolveStandardSourceRoot_PrefersProcessOutputDefinitions()
    {
        var root = Path.Combine(Path.GetTempPath(), "WorkAgentsDefinitionRoot_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "agents"));
        Directory.CreateDirectory(Path.Combine(root, "definitions", "agents"));

        try
        {
            Assert.Equal(Path.GetFullPath(root), FileBasedAgentLoader.ResolveStandardSourceRoot(root));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
