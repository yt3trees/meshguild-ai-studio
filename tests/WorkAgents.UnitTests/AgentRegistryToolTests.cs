using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.AI;
using WorkAgents.Agents;
using WorkAgents.Agents.Loading;
using WorkAgents.Agents.Tools;
using WorkAgents.Core;
using WorkAgents.Core.Abstractions;

namespace WorkAgents.UnitTests;

public sealed class AgentRegistryToolTests
{
    [Fact]
    public void Loader_maps_skills_and_camel_case_harness_settings()
    {
        var root = Path.Combine(Path.GetTempPath(), $"work-agents-{Guid.NewGuid():N}");
        var agentDirectory = Path.Combine(root, "agents", "repo-agent");
        Directory.CreateDirectory(Path.Combine(root, "skills", "shared-skill"));
        Directory.CreateDirectory(Path.Combine(agentDirectory, "skills", "local-skill"));
        Directory.CreateDirectory(agentDirectory);
        File.WriteAllText(Path.Combine(root, "skills", "shared-skill", "SKILL.md"), "---\nname: shared-skill\n---");
        File.WriteAllText(Path.Combine(agentDirectory, "skills", "local-skill", "SKILL.md"), "---\nname: local-skill\n---");
        File.WriteAllText(
            Path.Combine(agentDirectory, "agent.yaml"),
            """
            name: repo-agent
            displayName: Repository Agent
            skills:
              - shared-skill
              - missing-skill
            harness:
              shell: true
              fileStore: workspace
            """);

        try
        {
            var definition = Assert.Single(new FileBasedAgentLoader(Path.Combine(root, "agents")).Load());

            Assert.Equal("Repository Agent", definition.DisplayName);
            Assert.Equal(["shared-skill"], definition.SharedSkillNames);
            Assert.Equal(["local-skill"], definition.LocalSkillNames);
            Assert.True(definition.HarnessShell);
            Assert.Equal("workspace", definition.HarnessFileStore);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ListTools_returns_configured_tools_and_their_agents()
    {
        var registry = CreateRegistry(
        [
            Definition("shell-agent", harnessShell: true),
            Definition("workspace-agent", fileStore: "WORKSPACE"),
            Definition("full-agent", harnessShell: true, fileStore: "workspace"),
            Definition("artifact-agent", fileStore: "artifacts"),
            Definition("plain-agent"),
        ]);

        var tools = registry.ListTools();

        Assert.Equal(
        [
            "file_access_delete",
            "file_access_grep",
            "file_access_ls",
            "file_access_read",
            "file_access_replace",
            "file_access_replace_lines",
            "file_access_write",
            "file_memory_delete",
            "file_memory_grep",
            "file_memory_ls",
            "file_memory_read",
            "file_memory_replace",
            "file_memory_replace_lines",
            "file_memory_write",
            "mode_get",
            "mode_set",
            "run_shell",
            "todos_add",
            "todos_complete",
            "todos_get_all",
            "todos_get_remaining",
            "todos_remove",
        ],
            tools.Select(tool => tool.Name));

        var shell = Assert.Single(tools, tool => tool.Name == "run_shell");
        Assert.Equal("Microsoft.Agents.AI.Tools.Shell", shell.Source);
        Assert.Equal("required", shell.Approval);
        Assert.Equal(["shell-agent", "full-agent"], shell.Agents);

        var fileRead = Assert.Single(tools, tool => tool.Name == "file_access_read");
        Assert.Equal("Microsoft.Agents.AI.Hosting", fileRead.Source);
        Assert.Equal("automatic", fileRead.Approval);
        Assert.Equal(["shell-agent", "workspace-agent", "full-agent"], fileRead.Agents);
    }

    [Fact]
    public void ListTools_returns_empty_when_no_agent_enables_a_tool()
    {
        var registry = CreateRegistry(
        [
            Definition("artifact-agent", fileStore: "artifacts"),
            Definition("plain-agent"),
        ]);

        var tools = registry.ListTools();

        Assert.Empty(tools);
    }

    [Fact]
    public void ListTools_includes_custom_tools_for_their_agent_and_keeps_builtin_tools()
    {
        var definitions = new[]
        {
            Definition("meeting-agent", harnessShell: true),
            Definition("repo-agent", harnessShell: true),
        };
        var catalog = new AgentToolCatalog(
            definitions,
            [new TestToolProvider("meeting-agent", CreateTool("get_sss"))]);
        var registry = CreateRegistry(definitions, catalog);

        var tools = registry.ListTools();
        var custom = Assert.Single(tools, tool => tool.Name == "get_sss");
        Assert.Equal("custom", custom.Source);
        Assert.Equal("automatic", custom.Approval);
        Assert.Equal(["meeting-agent"], custom.Agents);

        var shell = Assert.Single(tools, tool => tool.Name == "run_shell");
        Assert.Equal(["meeting-agent", "repo-agent"], shell.Agents);
    }

    [Fact]
    public void ListAgents_exposes_attached_skill_content_with_local_skill_priority()
    {
        var root = Path.Combine(Path.GetTempPath(), $"work-agents-{Guid.NewGuid():N}");
        var agentDirectory = Path.Combine(root, "agents", "test-agent");
            var localSkillDirectory = Path.Combine(agentDirectory, "skills", "format");
            var sharedSkillDirectory = Path.Combine(root, "skills", "format");
            var sharedOnlySkillDirectory = Path.Combine(root, "team-source", "skills", "shared-only");
        Directory.CreateDirectory(localSkillDirectory);
        Directory.CreateDirectory(sharedSkillDirectory);
        Directory.CreateDirectory(sharedOnlySkillDirectory);
        File.WriteAllText(Path.Combine(localSkillDirectory, "SKILL.md"), "local skill content");
        File.WriteAllText(Path.Combine(sharedSkillDirectory, "SKILL.md"), "shared skill content");
        File.WriteAllText(Path.Combine(sharedOnlySkillDirectory, "SKILL.md"), "shared-only content");

        try
        {
            var registry = CreateRegistry(
            [
                new AgentDefinition
                {
                    Name = "test-agent",
                    FolderPath = agentDirectory,
                    LocalSkillNames = ["format"],
                    SharedSkillNames = ["format", "shared-only"],
                    SharedSkillPaths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["shared-only"] = sharedOnlySkillDirectory,
                    },
                },
            ]);

            var skills = Assert.Single(registry.ListAgents()).AttachedSkills;

            Assert.Collection(
                skills,
                local =>
                {
                    Assert.Equal("format", local.Name);
                    Assert.Equal("local", local.Source);
                    Assert.Equal("local skill content", local.Content);
                },
                shared =>
                {
                    Assert.Equal("shared-only", shared.Name);
                    Assert.Equal("shared", shared.Source);
                    Assert.Equal("shared-only content", shared.Content);
                });
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static AgentRegistry CreateRegistry(
        IReadOnlyList<AgentDefinition> definitions,
        AgentToolCatalog? toolCatalog = null)
        => new(
            definitions,
            new LlmAgentFactory(NullLogger<LlmAgentFactory>.Instance),
            new UnusedModelStore(),
            NullLogger<AgentRegistry>.Instance,
            toolCatalog: toolCatalog);

    private static AITool CreateTool(string name)
        => AIFunctionFactory.Create(
            (Func<CancellationToken, Task<string>>)(_ => Task.FromResult("ok")),
            name,
            $"{name} description",
            null);

    private static AgentDefinition Definition(
        string name,
        bool harnessShell = false,
        string? fileStore = null)
        => new()
        {
            Name = name,
            HarnessShell = harnessShell,
            HarnessFileStore = fileStore,
        };

    private sealed class UnusedModelStore : ILlmModelStore
    {
        public Task<IReadOnlyList<LlmModelSettings>> ListAsync(CancellationToken ct = default)
            => throw new InvalidOperationException("ListTools must not access the model store.");

        public Task<LlmModelSettings?> GetAsync(string id, CancellationToken ct = default)
            => throw new InvalidOperationException("ListTools must not access the model store.");

        public Task<LlmModelSettings?> ResolveForAgentAsync(string agentName, CancellationToken ct = default)
            => throw new InvalidOperationException("ListTools must not access the model store.");

        public Task SaveAsync(LlmModelSettings settings, string? apiKey, string? clientSecret = null, CancellationToken ct = default)
            => throw new InvalidOperationException("ListTools must not access the model store.");

        public Task DeleteAsync(string id, CancellationToken ct = default)
            => throw new InvalidOperationException("ListTools must not access the model store.");

        public Task<string?> GetAgentModelIdAsync(string agentName, CancellationToken ct = default)
            => throw new InvalidOperationException("ListTools must not access the model store.");

        public Task AssignAgentAsync(string agentName, string? modelId, CancellationToken ct = default)
            => throw new InvalidOperationException("ListTools must not access the model store.");
    }

    private sealed class TestToolProvider(string agentName, AITool tool) : IAgentToolProvider
    {
        public string AgentName { get; } = agentName;

        public IReadOnlyList<AgentToolRegistration> CreateTools(IServiceProvider services)
            => [new(tool.Name!, $"{tool.Name} description", "custom", "automatic", tool)];
    }
}
