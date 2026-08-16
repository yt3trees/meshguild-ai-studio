using WorkAgents.Core.Missions;
using WorkAgents.Infrastructure.Stores;
using WorkAgents.UnitTests.Support;

namespace WorkAgents.UnitTests.Stores;

public sealed class MissionWorkspaceStoreTests
{
    [Fact]
    public async Task RecordPreparedAsync_IsIdempotentForSameMission()
    {
        using var paths = new MissionWorkspaceTestPaths();
        var store = new SqliteMissionWorkspaceStore(paths.DatabasePath);

        await store.RecordPreparedAsync(new MissionWorkspaceRecord
        {
            MissionId = "mission-1",
            WorkspaceKey = "missions/mission-1/work",
            PreparedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1),
        });
        await store.RecordPreparedAsync(new MissionWorkspaceRecord
        {
            MissionId = "mission-1",
            WorkspaceKey = "missions/mission-1/work",
            PreparedAtUtc = DateTimeOffset.UtcNow,
        });

        var record = await store.GetAsync("mission-1");

        Assert.NotNull(record);
        Assert.Equal("missions/mission-1/work", record!.WorkspaceKey);
        Assert.Null(record.DeletedAtUtc);
    }
}
