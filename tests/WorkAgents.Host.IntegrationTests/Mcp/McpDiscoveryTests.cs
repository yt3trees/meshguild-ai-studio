using System.Net.Http.Headers;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace WorkAgents.Host.IntegrationTests.Mcp;

public sealed class McpDiscoveryTests
{
    [Fact]
    public async Task Discovery_AdvertisesToolsAndResources()
    {
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.UseSetting("Mcp:Enabled", "true"));
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/mcp")
        {
            Content = new StringContent("""
                {
                  "jsonrpc": "2.0",
                  "id": "discovery-1",
                  "method": "server/discover",
                  "params": {
                    "_meta": {
                      "io.modelcontextprotocol/protocolVersion": "2026-07-28",
                      "io.modelcontextprotocol/clientInfo": { "name": "test", "version": "1.0" },
                      "io.modelcontextprotocol/clientCapabilities": {}
                    }
                  }
                }
                """, Encoding.UTF8, "application/json"),
        };
        request.Headers.TryAddWithoutValidation("MCP-Protocol-Version", "2026-07-28");
        request.Headers.TryAddWithoutValidation("Mcp-Method", "server/discover");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

        using var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("tools", body, StringComparison.Ordinal);
        Assert.Contains("resources", body, StringComparison.Ordinal);
    }
}
