using System.Text.Json;
using WorkAgents.Core.Abstractions;
using WorkAgents.Core.Missions;
using WorkAgents.Core.Teams;
using WorkAgents.Infrastructure.Execution;
using WorkAgents.Infrastructure.Stores;
using WorkAgents.Orchestration;
using WorkAgents.Orchestration.Admission;
using WorkAgents.Orchestration.Teams;
using WorkAgents.UnitTests.Support;

namespace WorkAgents.UnitTests.Orchestration;

public sealed class MissionEngineTeamWorkspaceTests
{
    [Fact]
    public async Task TeamMission_HandsOffFileThroughOneMissionWorkspace()
    {
        using var paths = new MissionWorkspaceTestPaths();
        var messageStore = new SqliteMessageStore(paths.DatabasePath);
        var missions = new SqliteMissionStore(paths.DatabasePath);
        var workspaceStore = new SqliteMissionWorkspaceStore(paths.DatabasePath);
        var workspaceProvider = new MissionWorkspaceProvider(new MissionWorkspacePathResolver(paths.Root), workspaceStore);
        var invoker = new FileHandoffInvoker();
        var team = new TeamDefinition
        {
            Name = "handoff-team",
            Orchestrator = new TeamOrchestrator { Agent = "orchestrator" },
            Members = [new TeamMember { Agent = "writer" }, new TeamMember { Agent = "reader" }],
        };
        var executor = new TeamExecutor(invoker, new MessageBus(messageStore));
        var engine = new MissionEngine(
            missions,
            new AdmissionController(new SqliteMissionQueueStore(paths.DatabasePath), 5, 12),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<MissionEngine>.Instance,
            teamExecutor: executor,
            teams: [team],
            workspaceProvider: workspaceProvider);
        await missions.CreateAsync(new Mission
        {
            MissionId = "mission",
            Goal = "handoff a file",
            TargetKind = MissionTargetKind.Team,
            TargetName = team.Name,
        });

        await engine.StartAsync("mission");
        var execution = engine.GetExecutionTask("mission");
        if (execution is not null)
        {
            await execution;
        }

        var completed = await missions.GetAsync("mission");
        Assert.Equal(MissionStatus.Succeeded, completed!.Status);
        Assert.Equal("written", invoker.ReaderSaw);
        Assert.Equal(3, invoker.WorkingDirectories.Count);
        Assert.Single(invoker.WorkingDirectories.Distinct(StringComparer.OrdinalIgnoreCase));
        Assert.Equal(Path.Combine(paths.Root, "missions", "mission", "work"), invoker.WorkingDirectories[0]);
    }

    private sealed class FileHandoffInvoker : IAgentInvoker
    {
        public List<string> WorkingDirectories { get; } = [];

        public string? ReaderSaw { get; private set; }

        public Task<AgentInvocationResult> InvokeAsync(AgentInvocation invocation, CancellationToken ct = default)
        {
            WorkingDirectories.Add(invocation.WorkingDirectory ?? string.Empty);
            if (invocation.AgentName == "orchestrator")
            {
                return Task.FromResult(new AgentInvocationResult
                {
                    Utterance = "delegating",
                    ToolCalls =
                    [
                        new AgentToolCall { ToolName = "delegate_task", ArgsSummary = JsonSerializer.Serialize(new { agent = "writer", instruction = "write" }) },
                        new AgentToolCall { ToolName = "delegate_task", ArgsSummary = JsonSerializer.Serialize(new { agent = "reader", instruction = "read" }) },
                    ],
                });
            }

            if (invocation.AgentName == "writer")
            {
                Directory.CreateDirectory(invocation.WorkingDirectory!);
                File.WriteAllText(Path.Combine(invocation.WorkingDirectory!, "handoff.txt"), "written");
                return Task.FromResult(new AgentInvocationResult { Utterance = "file written" });
            }

            ReaderSaw = File.ReadAllText(Path.Combine(invocation.WorkingDirectory!, "handoff.txt"));
            return Task.FromResult(new AgentInvocationResult { Utterance = ReaderSaw });
        }
    }
}
