using Microsoft.Extensions.Options;
using WorkAgents.Core;
using WorkAgents.Host.Mcp;
using WorkAgents.Infrastructure.Stores;

namespace WorkAgents.Host.IntegrationTests.Mcp;

public sealed class McpApprovalTests
{
    [Fact]
    public async Task GetApproval_ProjectsPendingApprovalWithoutDecisionCapability()
    {
        var root = Path.Combine(Path.GetTempPath(), "work-agents-mcp-approval", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var store = new SqliteApprovalStore(Path.Combine(root, "state.db"));
            var approval = ApprovalRequest.Create("mission-1", "run_shell", "safe summary", TimeSpan.FromMinutes(5)) with
            {
                MissionId = "mission-1",
            };
            await store.CreateAsync(approval);
            var tool = new McpApprovalTools(store);

            var result = await tool.GetAsync("mission-1", approval.ApprovalId);

            Assert.Equal(approval.ApprovalId, result.ApprovalId);
            Assert.Equal("pending", result.Status);
            Assert.Equal("open_approval_ui", result.NextAction);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
