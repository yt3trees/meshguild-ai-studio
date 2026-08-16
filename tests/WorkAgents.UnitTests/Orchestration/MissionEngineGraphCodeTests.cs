using WorkAgents.Core.Graphs;
using WorkAgents.Core.Missions;
using WorkAgents.Infrastructure.Stores;
using WorkAgents.Infrastructure.Workflows;
using WorkAgents.Orchestration;
using WorkAgents.Orchestration.Admission;
using WorkAgents.Orchestration.Graph;
using WorkAgents.UnitTests.Fakes;

namespace WorkAgents.UnitTests.Orchestration;

public sealed class MissionEngineGraphCodeTests
{
    [Fact]
    public async Task ExecuteGraph_RunsCodeNodeScriptFile()
    {
        var root = Path.Combine(Path.GetTempPath(), "work-agents-tests", Guid.NewGuid().ToString("N"));
        var databasePath = Path.Combine(root, "state", "mission-graph-code.db");
        var graphFolder = Path.Combine(root, "graphs", "code-graph");
        Directory.CreateDirectory(graphFolder);
        await File.WriteAllTextAsync(
            Path.Combine(graphFolder, "script.csx"),
            "Inputs[\"input\"] + \"-scripted\"");
        try
        {
            var graph = new GraphDefinition
            {
                Name = "code-graph",
                Nodes = [new GraphNode { Id = "run", Kind = NodeKind.Code, Input = "hello", CodeFile = "script.csx" }],
                Edges = [],
                FolderPath = graphFolder,
            };

            var missions = new SqliteMissionStore(databasePath);
            var admission = new AdmissionController(new SqliteMissionQueueStore(databasePath), 5, 12);
            var graphExecutor = new GraphExecutor(new ScriptedAgentInvoker());
            var engine = new MissionEngine(
                missions,
                admission,
                Microsoft.Extensions.Logging.Abstractions.NullLogger<MissionEngine>.Instance,
                graphExecutor: graphExecutor,
                graphs: [graph],
                scriptRunner: new RoslynWorkflowScriptRunner());

            var mission = new Mission
            {
                MissionId = "mission",
                Goal = "goal",
                TargetKind = MissionTargetKind.Graph,
                TargetName = "code-graph",
                Status = MissionStatus.Queued,
            };
            await missions.CreateAsync(mission);

            await engine.StartAsync("mission");
            // GetExecutionTask may already be null here: the background execution can finish
            // (and remove itself from the tracking dictionary) before this line runs.
            var execution = engine.GetExecutionTask("mission");
            if (execution is not null)
            {
                await execution;
            }

            var completed = await missions.GetAsync("mission");
            Assert.Equal(MissionStatus.Succeeded, completed!.Status);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task ExecuteGraph_FailsMissionWhenCodeFileIsMissing()
    {
        var root = Path.Combine(Path.GetTempPath(), "work-agents-tests", Guid.NewGuid().ToString("N"));
        var databasePath = Path.Combine(root, "state", "mission-graph-code-missing.db");
        var graphFolder = Path.Combine(root, "graphs", "no-code-graph");
        Directory.CreateDirectory(graphFolder);
        try
        {
            var graph = new GraphDefinition
            {
                Name = "no-code-graph",
                Nodes = [new GraphNode { Id = "run", Kind = NodeKind.Code, Input = "hello" }],
                Edges = [],
                FolderPath = graphFolder,
            };

            var missions = new SqliteMissionStore(databasePath);
            var admission = new AdmissionController(new SqliteMissionQueueStore(databasePath), 5, 12);
            var graphExecutor = new GraphExecutor(new ScriptedAgentInvoker());
            var engine = new MissionEngine(
                missions,
                admission,
                Microsoft.Extensions.Logging.Abstractions.NullLogger<MissionEngine>.Instance,
                graphExecutor: graphExecutor,
                graphs: [graph],
                scriptRunner: new RoslynWorkflowScriptRunner());

            var mission = new Mission
            {
                MissionId = "mission",
                Goal = "goal",
                TargetKind = MissionTargetKind.Graph,
                TargetName = "no-code-graph",
                Status = MissionStatus.Queued,
            };
            await missions.CreateAsync(mission);

            await engine.StartAsync("mission");
            // GetExecutionTask may already be null here: the background execution can finish
            // (and remove itself from the tracking dictionary) before this line runs.
            var execution = engine.GetExecutionTask("mission");
            if (execution is not null)
            {
                await execution;
            }

            var completed = await missions.GetAsync("mission");
            Assert.Equal(MissionStatus.Failed, completed!.Status);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }
}
