using WorkAgents.Host.Mcp;

namespace WorkAgents.UnitTests.Mcp;

public sealed class McpResourceSecurityTests
{
    [Theory]
    [InlineData("mission-1", true)]
    [InlineData("artifact_2", true)]
    [InlineData("../outside", false)]
    [InlineData("C:\\secret.txt", false)]
    [InlineData("", false)]
    public void ResourcePolicy_ValidatesOpaqueIdentifiers(string value, bool expected)
        => Assert.Equal(expected, McpResourceAccessPolicy.IsSafeIdentifier(value));

    [Theory]
    [InlineData("text/plain", true)]
    [InlineData("application/json", true)]
    [InlineData("application/octet-stream", false)]
    [InlineData("application/x-secret", false)]
    public void ResourcePolicy_AllowsOnlyTextContentTypes(string contentType, bool expected)
        => Assert.Equal(expected, McpResourceAccessPolicy.IsTextContentType(contentType));
}
