using WorkAgents.Core.Graphs;
using WorkAgents.Infrastructure.Stores;
using WorkAgents.Orchestration.Graph;
using WorkAgents.UnitTests.Fakes;

namespace WorkAgents.UnitTests.Graphs;

public sealed class GraphVersionTests
{
    [Fact]
    public async Task Execute_ReusesVersionForTheSameDefinition()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), "work-agents-tests", Guid.NewGuid().ToString("N"), "graph.db");
        try
        {
            var graph = new GraphDefinition
            {
                Name = "graph",
                Nodes = [new GraphNode { Id = "one", Kind = NodeKind.Code, Input = "input", CodeFile = "one.csx" }],
                Edges = [],
            };
            var executor = new GraphExecutor(new ScriptedAgentInvoker(), new SqliteGraphVersionStore(databasePath));
            var first = await executor.ExecuteAsync(new GraphExecutionRequest { MissionId = "m1", Goal = "g", Graph = graph });
            var second = await executor.ExecuteAsync(new GraphExecutionRequest { MissionId = "m2", Goal = "g", Graph = graph });

            Assert.Equal(first.Version.GraphVersionId, second.Version.GraphVersionId);
            Assert.Equal(first.Version.ContentHash, second.Version.ContentHash);
        }
        finally
        {
            var directory = Path.GetDirectoryName(databasePath);
            if (directory is not null && Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }
}
