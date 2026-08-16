using Microsoft.Extensions.Logging.Abstractions;
using WorkAgents.Core.Missions;
using WorkAgents.Infrastructure.Stores;
using WorkAgents.Orchestration;
using WorkAgents.Orchestration.Admission;

namespace WorkAgents.UnitTests.Orchestration;

public sealed class MissionControlTests
{
    [Fact]
    public async Task PauseResumeAndAbortUseTheMissionStateMachine()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), "work-agents-tests", Guid.NewGuid().ToString("N"), "control.db");
        try
        {
            var missions = new SqliteMissionStore(databasePath);
            var admission = new AdmissionController(new SqliteMissionQueueStore(databasePath), 5, 12);
            var engine = new MissionEngine(missions, admission, NullLogger<MissionEngine>.Instance);
            await missions.CreateAsync(new Mission
            {
                MissionId = "mission",
                Goal = "goal",
                TargetKind = MissionTargetKind.Team,
                TargetName = "team",
                Status = MissionStatus.Running,
            });

            await engine.PauseAsync("mission");
            Assert.Equal(MissionStatus.Paused, (await missions.GetAsync("mission"))!.Status);
            await engine.ResumeAsync("mission");
            await engine.AbortAsync("mission");
            Assert.Equal(MissionStatus.Aborted, (await missions.GetAsync("mission"))!.Status);
        }
        finally
        {
            var directory = Path.GetDirectoryName(databasePath);
            if (directory is not null && Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }
}
