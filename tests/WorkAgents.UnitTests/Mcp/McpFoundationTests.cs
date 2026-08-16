using Microsoft.Extensions.Options;
using WorkAgents.Host.Mcp;
using WorkAgents.Infrastructure.Execution;

namespace WorkAgents.UnitTests.Mcp;

public sealed class McpFoundationTests
{
    [Fact]
    public void OptionsValidator_RejectsArtifactLimitAboveResponseLimit()
    {
        var result = new McpOptionsValidator().Validate(null, new McpOptions
        {
            MaxResponseBytes = 100,
            MaxArtifactBytes = 101,
        });

        Assert.False(result.Succeeded);
    }

    [Fact]
    public void RequestValidator_AllowsLoopbackAndRejectsExternalOrigin()
    {
        var validator = new McpRequestValidator(Options.Create(new McpOptions()));

        Assert.True(validator.IsOriginAllowed("http://localhost:5049"));
        Assert.True(validator.IsOriginAllowed("http://127.0.0.1:5160"));
        Assert.False(validator.IsOriginAllowed("https://example.test"));
    }

    [Fact]
    public void Redaction_RemovesSensitiveNames()
    {
        Assert.Equal("[redacted]", McpRedaction.SafeName("api-token"));
        Assert.Equal("safe-operation", McpRedaction.SafeName("safe-operation"));
    }

    [Fact]
    public void MissionCancellationRegistry_CancelsAndRemovesToken()
    {
        using var registry = new InMemoryMissionCancellationRegistry();
        var token = registry.Register("mission-1");

        Assert.False(token.IsCancellationRequested);
        Assert.True(registry.TryCancel("mission-1"));
        Assert.True(token.IsCancellationRequested);

        registry.Remove("mission-1");
        Assert.False(registry.TryCancel("mission-1"));
    }
}
