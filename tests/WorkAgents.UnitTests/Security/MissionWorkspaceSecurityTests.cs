using System.Text.Json;
using WorkAgents.Core.Missions;
using WorkAgents.Infrastructure.Execution;
using WorkAgents.Infrastructure.Stores;
using WorkAgents.UnitTests.Support;

namespace WorkAgents.UnitTests.Security;

public sealed class MissionWorkspaceSecurityTests
{
    [Fact]
    public async Task ReaderMetadataDoesNotExposeWorkspaceRootOrFileContents()
    {
        using var paths = new MissionWorkspaceTestPaths();
        var missions = new SqliteMissionStore(paths.DatabasePath);
        var workspaces = new SqliteMissionWorkspaceStore(paths.DatabasePath);
        await missions.CreateAsync(new Mission
        {
            MissionId = "mission",
            Goal = "test",
            TargetKind = MissionTargetKind.Team,
            TargetName = "team",
        });
        var workspace = paths.MissionWorkspace("mission");
        Directory.CreateDirectory(workspace);
        await File.WriteAllTextAsync(Path.Combine(workspace, "secret.txt"), "secret-file-content");
        await workspaces.RecordPreparedAsync(new MissionWorkspaceRecord
        {
            MissionId = "mission",
            WorkspaceKey = "missions/mission/work",
            PreparedAtUtc = DateTimeOffset.UtcNow,
        });
        var reader = new MissionWorkspaceReader(
            missions,
            workspaces,
            new MissionWorkspacePathResolver(paths.Root));

        var snapshot = await reader.ReadAsync("mission");
        var serialized = JsonSerializer.Serialize(snapshot);

        Assert.Contains("secret.txt", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-file-content", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain(paths.Root, serialized, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ReaderRejectsMissionPathTraversalBeforeLookingUpFiles()
    {
        using var paths = new MissionWorkspaceTestPaths();
        var reader = new MissionWorkspaceReader(
            new SqliteMissionStore(paths.DatabasePath),
            new SqliteMissionWorkspaceStore(paths.DatabasePath),
            new MissionWorkspacePathResolver(paths.Root));

        await Assert.ThrowsAsync<ArgumentException>(() => reader.ReadAsync("../outside"));
    }
}
