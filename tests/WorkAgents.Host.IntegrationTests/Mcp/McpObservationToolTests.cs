using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Options;
using WorkAgents.Core.Graphs;
using WorkAgents.Core.Loops;
using WorkAgents.Core.Missions;
using WorkAgents.Host.Mcp;
using WorkAgents.Infrastructure.Stores;

namespace WorkAgents.Host.IntegrationTests.Mcp;

public sealed class McpObservationToolTests
{
    [Fact]
    public async Task BuildGraph_ProjectsBoundedStateAndPagination()
    {
        var root = Path.Combine(Path.GetTempPath(), "work-agents-mcp-observation", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var database = Path.Combine(root, "state.db");
            var missions = new SqliteMissionStore(database);
            var graphs = new SqliteGraphVersionStore(database);
            var loops = new SqliteLoopStore(database);
            const string missionId = "mission-observation";
            await missions.CreateAsync(new Mission
            {
                MissionId = missionId,
                Goal = "observation test",
                TargetKind = MissionTargetKind.Graph,
                TargetName = "demo-graph",
                GraphVersionId = "graph-version-1",
                Status = MissionStatus.Running,
            });
            var version = await graphs.GetOrCreateVersionAsync(
                "demo-graph",
                "hash-1",
                number => new GraphVersion
                {
                    GraphVersionId = "graph-version-1",
                    GraphName = "demo-graph",
                    VersionNo = number,
                    ContentHash = "hash-1",
                    DefinitionYaml = "{}",
                });
            await graphs.CreateNodeRunAsync(new NodeRun
            {
                NodeRunId = "node-run-1",
                MissionId = missionId,
                NodeId = "first",
                NodeKind = NodeKind.Code,
                State = NodeRunState.Succeeded,
                OutputJson = "secret output must not be exposed",
            });
            await graphs.CreateNodeRunAsync(new NodeRun
            {
                NodeRunId = "node-run-2",
                MissionId = missionId,
                NodeId = "second",
                NodeKind = NodeKind.Agent,
                State = NodeRunState.Running,
                InputJson = "secret input must not be exposed",
            });
            await graphs.RecordEdgeTransitAsync(new EdgeTransit
            {
                EdgeTransitId = "edge-transit-1",
                MissionId = missionId,
                EdgeId = "first-to-second",
                FromNodeRunId = "node-run-1",
                ToNodeRunId = "node-run-2",
                ConditionResult = "true",
            });
            await loops.CreateLoopRunAsync(new LoopRun
            {
                LoopRunId = "loop-1",
                MissionId = missionId,
                NodeRunId = "node-run-2",
                MaxIterations = 2,
            });
            await loops.CreateIterationAsync(new Iteration
            {
                IterationId = "iteration-1",
                LoopRunId = "loop-1",
                IterationNo = 1,
                State = IterationState.Running,
                OutputJson = "secret iteration output",
            });

            var tool = new McpObservationTools(
                missions,
                graphs,
                loops,
                new McpRequestValidator(Options.Create(new McpOptions { MaxPageSize = 1 })));

            var observation = await tool.BuildAsync(missionId, 0);

            Assert.Equal(missionId, observation.MissionId);
            Assert.Equal("demo-graph", observation.GraphName);
            Assert.Equal(version.VersionNo, observation.GraphVersionNo);
            Assert.True(observation.IsPartial);
            Assert.Single(observation.Nodes);
            Assert.Equal("first", observation.Nodes[0].NodeId);
            Assert.NotNull(observation.NextOffset);
            Assert.DoesNotContain("secret", System.Text.Json.JsonSerializer.Serialize(observation), StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task GetGraph_RejectsUnknownMissionWithSafeToolError()
    {
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.UseSetting("Mcp:Enabled", "true"));
        using var client = factory.CreateClient();
        var body = """
            {
              "jsonrpc": "2.0",
              "id": "graph-test-1",
              "method": "tools/call",
              "params": {
                "name": "workagents_get_graph",
                "arguments": { "missionId": "missing-mission" },
                "_meta": {
                  "io.modelcontextprotocol/protocolVersion": "2026-07-28",
                  "io.modelcontextprotocol/clientInfo": { "name": "test", "version": "1.0" },
                  "io.modelcontextprotocol/clientCapabilities": {}
                }
              }
            }
            """;
        using var request = new HttpRequestMessage(HttpMethod.Post, "/mcp")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        request.Headers.TryAddWithoutValidation("MCP-Protocol-Version", "2026-07-28");
        request.Headers.TryAddWithoutValidation("Mcp-Method", "tools/call");
        request.Headers.TryAddWithoutValidation("Mcp-Name", "workagents_get_graph");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

        using var response = await client.SendAsync(request);
        var responseBody = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("isError", responseBody, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("mission_not_found", responseBody, StringComparison.OrdinalIgnoreCase);
    }
}
