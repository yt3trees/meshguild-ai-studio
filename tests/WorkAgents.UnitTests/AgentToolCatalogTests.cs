using Microsoft.Extensions.AI;
using WorkAgents.Agents.Loading;
using WorkAgents.Agents.Tools;

namespace WorkAgents.UnitTests;

public sealed class AgentToolCatalogTests
{
    [Fact]
    public void Empty_provider_returns_empty_catalog()
    {
        var catalog = new AgentToolCatalog(
            [Definition("meeting-agent")],
            Array.Empty<IAgentToolProvider>());

        Assert.Empty(catalog.GetTools("meeting-agent"));
        Assert.Empty(catalog.GetRegistrations("meeting-agent"));
    }

    [Fact]
    public void Assembly_provider_is_created_and_assigned_case_insensitively()
    {
        var catalog = new AgentToolCatalog(
            new NullServiceProvider(),
            [Definition("meeting-agent"), Definition("repo-agent")]);

        var registration = Assert.Single(catalog.GetRegistrations("MEETING-AGENT"));

        Assert.Equal("get_sss", registration.Name);
        Assert.Equal("custom", registration.Source);
        Assert.Equal("get_sss", Assert.Single(catalog.GetTools("meeting-agent")).Name);
        Assert.Empty(catalog.GetTools("repo-agent"));
    }

    [Fact]
    public void Tool_names_are_sorted_without_relying_on_provider_order()
    {
        var provider = new StaticToolProvider(
            "meeting-agent",
            Registration("zeta_tool"),
            Registration("alpha_tool"));
        var catalog = new AgentToolCatalog([Definition("meeting-agent")], [provider]);

        Assert.Equal(
            ["alpha_tool", "zeta_tool"],
            catalog.GetRegistrations("meeting-agent").Select(registration => registration.Name));
    }

    [Fact]
    public void Duplicate_tool_names_for_one_agent_are_rejected()
    {
        var provider = new StaticToolProvider(
            "meeting-agent",
            Registration("duplicate_tool"),
            Registration("duplicate_tool"));

        var exception = Assert.Throws<InvalidOperationException>(() =>
            new AgentToolCatalog([Definition("meeting-agent")], [provider]));

        Assert.Contains("duplicate_tool", exception.Message, StringComparison.Ordinal);
        Assert.Contains("meeting-agent", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Same_tool_name_is_allowed_for_different_agents()
    {
        var catalog = new AgentToolCatalog(
            [Definition("meeting-agent"), Definition("repo-agent")],
            [
                new StaticToolProvider("meeting-agent", Registration("shared_tool")),
                new StaticToolProvider("repo-agent", Registration("shared_tool")),
            ]);

        Assert.Equal("shared_tool", Assert.Single(catalog.GetTools("meeting-agent")).Name);
        Assert.Equal("shared_tool", Assert.Single(catalog.GetTools("repo-agent")).Name);
    }

    [Fact]
    public void Unknown_agent_provider_is_rejected()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            new AgentToolCatalog(
                [Definition("meeting-agent")],
                [new StaticToolProvider("missing-agent", Registration("missing_tool"))]));

        Assert.Contains("StaticToolProvider", exception.Message, StringComparison.Ordinal);
        Assert.Contains("missing-agent", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Empty_tool_name_is_rejected()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            new AgentToolCatalog(
                [Definition("meeting-agent")],
                [new StaticToolProvider("meeting-agent", Registration(""))]));

        Assert.Contains("empty tool name", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Empty_description_is_rejected()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            new AgentToolCatalog(
                [Definition("meeting-agent")],
                [new StaticToolProvider("meeting-agent", Registration("valid_tool", " "))]));

        Assert.Contains("empty description", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Tool_creation_exception_contains_provider_type_and_agent_name()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            new AgentToolCatalog(
                [Definition("meeting-agent")],
                [new ThrowingToolProvider("meeting-agent")]));

        Assert.Contains(nameof(ThrowingToolProvider), exception.Message, StringComparison.Ordinal);
        Assert.Contains("meeting-agent", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Harness_builtin_tool_name_collision_is_rejected()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            new AgentToolCatalog(
                [Definition("repo-agent", harnessShell: true)],
                [new StaticToolProvider("repo-agent", Registration("run_shell"))]));

        Assert.Contains("collides with a Harness built-in tool", exception.Message, StringComparison.Ordinal);
        Assert.Contains("run_shell", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Required_tool_is_wrapped_for_the_existing_approval_flow()
    {
        var tool = AIFunctionFactory.Create(
            (Func<CancellationToken, Task<string>>)(_ => Task.FromResult("ok")),
            "approval_tool",
            "Requires approval.",
            null);
        var provider = new StaticToolProvider(
            "meeting-agent",
            new AgentToolRegistration("approval_tool", "Requires approval.", "custom", "required", tool));
        var catalog = new AgentToolCatalog([Definition("meeting-agent")], [provider]);

        Assert.IsType<ApprovalRequiredAIFunction>(Assert.Single(catalog.GetTools("meeting-agent")));
    }

    private static AgentDefinition Definition(string name, bool harnessShell = false)
        => new()
        {
            Name = name,
            HarnessShell = harnessShell,
        };

    private static AgentToolRegistration Registration(string name, string description = "Tool description")
    {
        var functionName = string.IsNullOrWhiteSpace(name) ? "valid_tool" : name;
        var tool = AIFunctionFactory.Create(
            (Func<CancellationToken, Task<string>>)(_ => Task.FromResult("ok")),
            functionName,
            description,
            null);
        return new AgentToolRegistration(name, description, "custom", "automatic", tool);
    }

    private sealed class StaticToolProvider : IAgentToolProvider
    {
        private readonly IReadOnlyList<AgentToolRegistration> _registrations;

        public StaticToolProvider(string agentName, params AgentToolRegistration[] registrations)
        {
            AgentName = agentName;
            _registrations = registrations;
        }

        public string AgentName { get; }

        public IReadOnlyList<AgentToolRegistration> CreateTools(IServiceProvider services)
            => _registrations;
    }

    private sealed class ThrowingToolProvider(string agentName) : IAgentToolProvider
    {
        public string AgentName { get; } = agentName;

        public IReadOnlyList<AgentToolRegistration> CreateTools(IServiceProvider services)
            => throw new InvalidOperationException("provider failure");
    }

    private sealed class NullServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }
}