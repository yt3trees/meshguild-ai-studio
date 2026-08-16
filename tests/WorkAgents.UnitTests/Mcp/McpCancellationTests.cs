using WorkAgents.Infrastructure.Execution;

namespace WorkAgents.UnitTests.Mcp;

public sealed class McpCancellationTests
{
    [Fact]
    public void ExplicitCancellation_IsDifferentFromRemovingTheRegistration()
    {
        using var registry = new InMemoryMissionCancellationRegistry();
        var token = registry.Register("mission-cancel");

        Assert.True(registry.TryCancel("mission-cancel"));
        Assert.True(token.IsCancellationRequested);
        registry.Remove("mission-cancel");
        Assert.False(registry.TryCancel("mission-cancel"));
    }
}
