using WorkAgents.Core.Missions;
using WorkAgents.Infrastructure.Execution;
using WorkAgents.Infrastructure.Stores;
using Microsoft.Extensions.Logging.Abstractions;
using WorkAgents.Core.Abstractions;
using WorkAgents.Orchestration;
using WorkAgents.Orchestration.Admission;

namespace WorkAgents.UnitTests.Orchestration;

public sealed class MissionRecoveryTests
{
    [Fact]
    public async Task RecoverWithoutCheckpoint_ConvergesToFailedNoCheckpoint()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), "work-agents-tests", Guid.NewGuid().ToString("N"), "recovery.db");
        try
        {
            var missions = new SqliteMissionStore(databasePath);
            await missions.CreateAsync(new Mission
            {
                MissionId = "mission",
                Goal = "goal",
                TargetKind = MissionTargetKind.Team,
                TargetName = "team",
                Status = MissionStatus.Running,
            });
            var service = new MissionRecoveryHostedService(missions, new SqliteCheckpointStore(databasePath), NullLogger<MissionRecoveryHostedService>.Instance);

            await service.RecoverAsync();

            var mission = await missions.GetAsync("mission");
            Assert.Equal(MissionStatus.Failed, mission!.Status);
            Assert.Equal(MissionStopReason.NoCheckpoint, mission.StopReason);
        }
        finally
        {
            var directory = Path.GetDirectoryName(databasePath);
            if (directory is not null && Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }
}
