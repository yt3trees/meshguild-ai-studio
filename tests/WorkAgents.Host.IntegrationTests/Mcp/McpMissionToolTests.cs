using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using WorkAgents.Core.Abstractions;
using WorkAgents.Core.Missions;
using WorkAgents.Host.Mcp;
using WorkAgents.Infrastructure.Execution;
using WorkAgents.Infrastructure.Stores;
using WorkAgents.Orchestration;
using WorkAgents.Orchestration.Admission;

namespace WorkAgents.Host.IntegrationTests.Mcp;

public sealed class McpMissionToolTests
{
    [Fact]
    public async Task SubmitMission_ReturnsAcceptedMissionHandle()
    {
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.UseSetting("Mcp:Enabled", "true"));
        using var client = factory.CreateClient();
        using var request = CreateToolRequest("workagents_submit_mission", """
            {
              "requestKey": "integration-submit-1",
              "goal": "MCP integration smoke test",
              "targetKind": "Graph",
              "targetName": "demo-graph",
              "budget": { "timeLimitSeconds": 30, "maxIterations": 1 }
            }
            """);

        using var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("missionId", body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("status", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SubmitMission_RejectsUnknownTargetWithoutCreatingMission()
    {
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.UseSetting("Mcp:Enabled", "true"));
        using var client = factory.CreateClient();
        using var request = CreateToolRequest("workagents_submit_mission", """
            {
              "requestKey": "integration-invalid-1",
              "goal": "invalid target test",
              "targetKind": "Graph",
              "targetName": "does-not-exist"
            }
            """);

        using var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("isError", body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("unknown_target", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CancelMission_AbortsAcceptedMission()
    {
        var root = Path.Combine(Path.GetTempPath(), "work-agents-mcp-cancel", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var database = Path.Combine(root, "state.db");
            var missions = new SqliteMissionStore(database);
            var queue = new SqliteMissionQueueStore(database);
            var admission = new AdmissionController(queue, maxConcurrentMissions: 1, maxConcurrentAgents: 1);
            var cancellation = new InMemoryMissionCancellationRegistry();
            var engine = new MissionEngine(
                missions,
                admission,
                NullLogger<MissionEngine>.Instance,
                cancellationRegistry: cancellation);
            var tool = new McpMissionTools(
                engine,
                missions,
                new SqliteMcpSubmissionStore(database),
                [],
                [],
                new McpRequestValidator(Options.Create(new McpOptions())),
                new McpAuditLogger(NullLogger<McpAuditLogger>.Instance),
                Options.Create(new McpOptions()));
            await missions.CreateAsync(new Mission
            {
                MissionId = "mission-cancel-direct",
                Goal = "MCP cancellation smoke test",
                TargetKind = MissionTargetKind.Graph,
                TargetName = "demo-graph",
                Status = MissionStatus.Queued,
            });

            var result = await tool.workagents_cancel_mission("mission-cancel-direct", "test cancellation");

            Assert.Equal("aborted", result.Status);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static HttpRequestMessage CreateToolRequest(string name, string arguments)
    {
        var body = $$"""
            {
              "jsonrpc": "2.0",
              "id": "mission-test-1",
              "method": "tools/call",
              "params": {
                "name": "{{name}}",
                "arguments": {{arguments}},
                "_meta": {
                  "io.modelcontextprotocol/protocolVersion": "2026-07-28",
                  "io.modelcontextprotocol/clientInfo": { "name": "test", "version": "1.0" },
                  "io.modelcontextprotocol/clientCapabilities": {}
                }
              }
            }
            """;
        var request = new HttpRequestMessage(HttpMethod.Post, "/mcp")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        request.Headers.TryAddWithoutValidation("MCP-Protocol-Version", "2026-07-28");
        request.Headers.TryAddWithoutValidation("Mcp-Method", "tools/call");
        request.Headers.TryAddWithoutValidation("Mcp-Name", name);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        return request;
    }

}
