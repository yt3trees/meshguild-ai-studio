using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace WorkAgents.Host.IntegrationTests.Mcp;

public sealed class McpProtocolSmokeTests
{
    [Fact]
    public async Task DisabledEndpoint_IsNotMapped()
    {
        await using var factory = CreateFactory(enabled: false);
        using var client = factory.CreateClient();

        using var response = await client.SendAsync(CreateRequest());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ModernDiscovery_ReturnsCapabilities()
    {
        await using var factory = CreateFactory(enabled: true);
        using var client = factory.CreateClient();

        using var request = CreateRequest();
        request.Headers.TryAddWithoutValidation("MCP-Protocol-Version", "2026-07-28");
        request.Headers.TryAddWithoutValidation("Mcp-Method", "server/discover");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

        using var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("2026-07-28", body, StringComparison.Ordinal);
        Assert.Contains("supportedVersions", body, StringComparison.Ordinal);
    }

    private static WebApplicationFactory<Program> CreateFactory(bool enabled)
        => new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Mcp:Enabled", enabled.ToString());
            builder.UseSetting("Profile", "Local");
        });

    private static HttpRequestMessage CreateRequest()
    {
        const string body = """
            {
              "jsonrpc": "2.0",
              "id": "smoke-1",
              "method": "server/discover",
              "params": {
                "_meta": {
                  "io.modelcontextprotocol/protocolVersion": "2026-07-28",
                  "io.modelcontextprotocol/clientInfo": { "name": "test", "version": "1.0" },
                  "io.modelcontextprotocol/clientCapabilities": {}
                }
              }
            }
            """;

        return new HttpRequestMessage(HttpMethod.Post, "/mcp")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
    }
}
