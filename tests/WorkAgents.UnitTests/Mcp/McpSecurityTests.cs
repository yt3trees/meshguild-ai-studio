using WorkAgents.Host.Mcp;

namespace WorkAgents.UnitTests.Mcp;

public sealed class McpSecurityTests
{
    [Fact]
    public void OptionsValidator_RejectsWildcardOrigins()
    {
        var result = new McpOptionsValidator().Validate(null, new McpOptions
        {
            AllowedOrigins = ["*"],
        });

        Assert.False(result.Succeeded);
    }

    [Fact]
    public void AuditSafeNames_DoNotExposeTokenLikeValues()
    {
        Assert.Equal("[redacted]", McpRedaction.SafeName("access-token"));
        Assert.Equal("operation-1", McpRedaction.SafeName("operation-1"));
    }

    [Fact]
    public void ResourcePolicy_RejectsAbsoluteAndTraversalIdentifiers()
    {
        Assert.False(McpResourceAccessPolicy.IsSafeIdentifier("C:\\work-agents\\secret.txt"));
        Assert.False(McpResourceAccessPolicy.IsSafeIdentifier("mission/../other"));
        Assert.True(McpResourceAccessPolicy.IsSafeIdentifier("mission-01"));
    }
}
