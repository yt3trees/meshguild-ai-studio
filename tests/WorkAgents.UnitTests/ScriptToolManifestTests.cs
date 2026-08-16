using WorkAgents.Agents.Tools;

namespace WorkAgents.UnitTests;

public sealed class ScriptToolManifestTests
{
    private const string ManifestPath = "send_slack.tool.yaml";

    [Fact]
    public void Deserialize_ValidManifest_Succeeds()
    {
        var yaml = """
            name: send_slack_message
            description: Slackにメッセージを送信する
            agentName: sales-report-agent
            runtime: node
            entryPoint: send_slack.js
            approval: required
            timeoutSeconds: 15
            parameters:
              type: object
            allowedHosts:
              - hooks.slack.com
            """;

        var manifest = ScriptToolManifestSerializer.Deserialize(yaml, ManifestPath);

        Assert.Equal("send_slack_message", manifest.Name);
        Assert.Equal("sales-report-agent", manifest.AgentName);
        Assert.Equal(ScriptToolRuntime.Node, manifest.Runtime);
        Assert.Equal("send_slack.js", manifest.EntryPoint);
        Assert.Equal("required", manifest.Approval);
        Assert.Equal(15, manifest.TimeoutSeconds);
        Assert.Equal(["hooks.slack.com"], manifest.AllowedHosts);
    }

    [Fact]
    public void Deserialize_DefaultsTimeoutTo30Seconds()
    {
        var yaml = """
            name: lookup_customer
            description: 顧客情報を照会する
            agentName: sales-report-agent
            runtime: python
            entryPoint: lookup_customer.py
            approval: automatic
            """;

        var manifest = ScriptToolManifestSerializer.Deserialize(yaml, ManifestPath);

        Assert.Equal(30, manifest.TimeoutSeconds);
        Assert.Equal(ScriptToolRuntime.Python, manifest.Runtime);
        Assert.Empty(manifest.AllowedHosts);
    }

    [Theory]
    [InlineData("""
        description: desc
        agentName: a
        runtime: node
        entryPoint: x.js
        approval: automatic
        """)]
    [InlineData("""
        name: x
        agentName: a
        runtime: node
        entryPoint: x.js
        approval: automatic
        """)]
    [InlineData("""
        name: x
        description: desc
        runtime: node
        entryPoint: x.js
        approval: automatic
        """)]
    [InlineData("""
        name: x
        description: desc
        agentName: a
        entryPoint: x.js
        approval: automatic
        """)]
    [InlineData("""
        name: x
        description: desc
        agentName: a
        runtime: node
        approval: automatic
        """)]
    [InlineData("""
        name: x
        description: desc
        agentName: a
        runtime: node
        entryPoint: x.js
        """)]
    public void Deserialize_MissingRequiredField_Throws(string yaml)
    {
        Assert.Throws<ScriptToolManifestValidationException>(() => ScriptToolManifestSerializer.Deserialize(yaml, ManifestPath));
    }

    [Fact]
    public void Deserialize_InvalidRuntime_Throws()
    {
        var yaml = """
            name: x
            description: desc
            agentName: a
            runtime: ruby
            entryPoint: x.rb
            approval: automatic
            """;

        var ex = Assert.Throws<ScriptToolManifestValidationException>(() => ScriptToolManifestSerializer.Deserialize(yaml, ManifestPath));
        Assert.Contains("runtime", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Deserialize_InvalidApproval_Throws()
    {
        var yaml = """
            name: x
            description: desc
            agentName: a
            runtime: node
            entryPoint: x.js
            approval: maybe
            """;

        var ex = Assert.Throws<ScriptToolManifestValidationException>(() => ScriptToolManifestSerializer.Deserialize(yaml, ManifestPath));
        Assert.Contains("approval", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Deserialize_UnknownKey_Throws()
    {
        var yaml = """
            name: x
            description: desc
            agentName: a
            runtime: node
            entryPoint: x.js
            approval: automatic
            unknownField: oops
            """;

        Assert.Throws<ScriptToolManifestValidationException>(() => ScriptToolManifestSerializer.Deserialize(yaml, ManifestPath));
    }

    [Fact]
    public void Deserialize_NonPositiveTimeout_Throws()
    {
        var yaml = """
            name: x
            description: desc
            agentName: a
            runtime: node
            entryPoint: x.js
            approval: automatic
            timeoutSeconds: 0
            """;

        Assert.Throws<ScriptToolManifestValidationException>(() => ScriptToolManifestSerializer.Deserialize(yaml, ManifestPath));
    }
}
