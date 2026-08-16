using WorkAgents.Agents.Tools;

namespace WorkAgents.UnitTests;

public sealed class ToolPluginHostAllowlistTests
{
    [Fact]
    public void EmptyAllowlist_AllowsAnyHost()
    {
        var allowlist = new ToolPluginHostAllowlist([]);

        Assert.True(allowlist.IsAllowed("intranet-api.example.local"));
        allowlist.EnsureAllowed("intranet-api.example.local");
    }

    [Fact]
    public void NonEmptyAllowlist_AllowsListedHostOnly()
    {
        var allowlist = new ToolPluginHostAllowlist(["intranet-api.example.local"]);

        Assert.True(allowlist.IsAllowed("intranet-api.example.local"));
        Assert.False(allowlist.IsAllowed("other-host.example.com"));
    }

    [Fact]
    public void EnsureAllowed_DisallowedHost_Throws()
    {
        var allowlist = new ToolPluginHostAllowlist(["intranet-api.example.local"]);

        var ex = Assert.Throws<InvalidOperationException>(() => allowlist.EnsureAllowed("other-host.example.com"));
        Assert.Contains("other-host.example.com", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void HostComparison_IsCaseInsensitive()
    {
        var allowlist = new ToolPluginHostAllowlist(["Intranet-API.example.local"]);

        Assert.True(allowlist.IsAllowed("intranet-api.example.local"));
    }
}
