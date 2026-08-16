using WorkAgents.Core.Missions;
using WorkAgents.Infrastructure.Execution;
using WorkAgents.Infrastructure.Stores;
using WorkAgents.UnitTests.Support;

namespace WorkAgents.UnitTests.Stores;

public sealed class MissionWorkspaceReaderTests
{
    [Fact]
    public async Task ReadAsync_ReturnsSortedRelativeMetadataWithoutFileContents()
    {
        using var paths = new MissionWorkspaceTestPaths();
        var reader = await CreateReaderAsync(paths, "mission");
        var workspace = paths.MissionWorkspace("mission");
        Directory.CreateDirectory(Path.Combine(workspace, "reports"));
        await File.WriteAllTextAsync(Path.Combine(workspace, "reports", "z.txt"), "z-content");
        await File.WriteAllTextAsync(Path.Combine(workspace, "a.txt"), "a-content");

        var snapshot = await reader.ReadAsync("mission");

        Assert.Equal(MissionWorkspaceState.Available, snapshot.State);
        Assert.Equal(["a.txt", "reports", "reports/z.txt"], snapshot.Items.Select(item => item.RelativePath));
        var file = snapshot.Items.Single(item => item.RelativePath == "reports/z.txt");
        Assert.Equal(WorkspaceEntryKind.File, file.Kind);
        Assert.Equal(9, file.SizeBytes);
        Assert.Equal(WorkspaceEntryStatus.Available, file.Status);
    }

    [Fact]
    public async Task ReadAsync_DistinguishesNotCreatedEmptyAndDeletedStates()
    {
        using var paths = new MissionWorkspaceTestPaths();
        var missions = new SqliteMissionStore(paths.DatabasePath);
        var workspaces = new SqliteMissionWorkspaceStore(paths.DatabasePath);
        await missions.CreateAsync(new Mission
        {
            MissionId = "not-created",
            Goal = "test",
            TargetKind = MissionTargetKind.Team,
            TargetName = "team",
        });
        await missions.CreateAsync(new Mission
        {
            MissionId = "empty",
            Goal = "test",
            TargetKind = MissionTargetKind.Team,
            TargetName = "team",
        });
        await missions.CreateAsync(new Mission
        {
            MissionId = "deleted",
            Goal = "test",
            TargetKind = MissionTargetKind.Team,
            TargetName = "team",
        });
        var deletedPath = paths.MissionWorkspace("deleted");
        Directory.CreateDirectory(paths.MissionWorkspace("empty"));
        Directory.CreateDirectory(deletedPath);
        await workspaces.RecordPreparedAsync(new MissionWorkspaceRecord
        {
            MissionId = "empty",
            WorkspaceKey = "missions/empty/work",
            PreparedAtUtc = DateTimeOffset.UtcNow,
        });
        await workspaces.RecordPreparedAsync(new MissionWorkspaceRecord
        {
            MissionId = "deleted",
            WorkspaceKey = "missions/deleted/work",
            PreparedAtUtc = DateTimeOffset.UtcNow,
        });
        await workspaces.MarkDeletedAsync("deleted", DateTimeOffset.UtcNow);
        var reader = new MissionWorkspaceReader(
            missions,
            workspaces,
            new MissionWorkspacePathResolver(paths.Root));

        Assert.Equal(MissionWorkspaceState.NotCreated, (await reader.ReadAsync("not-created")).State);
        Assert.Equal(MissionWorkspaceState.Empty, (await reader.ReadAsync("empty")).State);
        Assert.Equal(MissionWorkspaceState.Deleted, (await reader.ReadAsync("deleted")).State);
        await Assert.ThrowsAsync<KeyNotFoundException>(() => reader.ReadAsync("unknown"));
    }

    [Fact]
    public async Task ReadAsync_IsolatesMissionsAndReportsLargeFileMetadata()
    {
        using var paths = new MissionWorkspaceTestPaths();
        var missions = new SqliteMissionStore(paths.DatabasePath);
        var workspaces = new SqliteMissionWorkspaceStore(paths.DatabasePath);
        foreach (var missionId in new[] { "mission-a", "mission-b" })
        {
            await missions.CreateAsync(new Mission
            {
                MissionId = missionId,
                Goal = "test",
                TargetKind = MissionTargetKind.Team,
                TargetName = "team",
            });
            Directory.CreateDirectory(paths.MissionWorkspace(missionId));
            await workspaces.RecordPreparedAsync(new MissionWorkspaceRecord
            {
                MissionId = missionId,
                WorkspaceKey = $"missions/{missionId}/work",
                PreparedAtUtc = DateTimeOffset.UtcNow,
            });
        }
        await File.WriteAllBytesAsync(Path.Combine(paths.MissionWorkspace("mission-a"), "large.bin"), new byte[4096]);
        await File.WriteAllTextAsync(Path.Combine(paths.MissionWorkspace("mission-b"), "other.txt"), "other");
        var reader = new MissionWorkspaceReader(
            missions,
            workspaces,
            new MissionWorkspacePathResolver(paths.Root));

        var snapshot = await reader.ReadAsync("mission-a");

        Assert.Contains(snapshot.Items, item => item.RelativePath == "large.bin" && item.SizeBytes == 4096);
        Assert.DoesNotContain(snapshot.Items, item => item.RelativePath == "other.txt");
    }

    private static async Task<MissionWorkspaceReader> CreateReaderAsync(MissionWorkspaceTestPaths paths, string missionId)
    {
        var missions = new SqliteMissionStore(paths.DatabasePath);
        var workspaces = new SqliteMissionWorkspaceStore(paths.DatabasePath);
        await missions.CreateAsync(new Mission
        {
            MissionId = missionId,
            Goal = "test",
            TargetKind = MissionTargetKind.Team,
            TargetName = "team",
        });
        Directory.CreateDirectory(paths.MissionWorkspace(missionId));
        await workspaces.RecordPreparedAsync(new MissionWorkspaceRecord
        {
            MissionId = missionId,
            WorkspaceKey = $"missions/{missionId}/work",
            PreparedAtUtc = DateTimeOffset.UtcNow,
        });
        return new MissionWorkspaceReader(missions, workspaces, new MissionWorkspacePathResolver(paths.Root));
    }
}
