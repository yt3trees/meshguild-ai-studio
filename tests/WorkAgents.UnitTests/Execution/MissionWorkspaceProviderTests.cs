using WorkAgents.Core.Missions;
using WorkAgents.Infrastructure.Execution;
using WorkAgents.Infrastructure.Stores;
using WorkAgents.UnitTests.Support;

namespace WorkAgents.UnitTests.Execution;

public sealed class MissionWorkspaceProviderTests
{
    [Fact]
    public void ResolvePath_UsesMissionScopedWorkDirectoryAndRejectsTraversal()
    {
        using var paths = new MissionWorkspaceTestPaths();
        var resolver = new MissionWorkspacePathResolver(paths.Root);

        var resolved = resolver.ResolvePath("mission-1");

        Assert.Equal(Path.Combine(paths.Root, "missions", "mission-1", "work"), resolved);
        Assert.Equal("missions/mission-1/work", resolver.ResolveWorkspaceKey("mission-1"));
        Assert.Throws<ArgumentException>(() => resolver.ResolvePath("../other"));
        Assert.Throws<ArgumentException>(() => resolver.ResolvePath($"bad{Path.DirectorySeparatorChar}id"));
        Assert.Throws<ArgumentException>(() => resolver.ResolvePath(Path.GetFullPath("outside")));
    }

    [Fact]
    public async Task PrepareAsync_CreatesDirectoryAndPersistsOneDescriptor()
    {
        using var paths = new MissionWorkspaceTestPaths();
        var store = new SqliteMissionWorkspaceStore(paths.DatabasePath);
        var provider = new MissionWorkspaceProvider(new MissionWorkspacePathResolver(paths.Root), store);

        var first = await provider.PrepareAsync("mission-1");
        var second = await provider.PrepareAsync("mission-1");
        var record = await store.GetAsync("mission-1");

        Assert.Equal(first, second);
        Assert.True(Directory.Exists(first));
        Assert.NotNull(record);
        Assert.Equal("missions/mission-1/work", record!.WorkspaceKey);
        Assert.Null(record.DeletedAtUtc);
    }

    [Fact]
    public async Task Store_PreservesDeletedState()
    {
        using var paths = new MissionWorkspaceTestPaths();
        var store = new SqliteMissionWorkspaceStore(paths.DatabasePath);
        var preparedAt = DateTimeOffset.UtcNow.AddMinutes(-1);
        var deletedAt = DateTimeOffset.UtcNow;

        await store.RecordPreparedAsync(new MissionWorkspaceRecord
        {
            MissionId = "mission-1",
            WorkspaceKey = "missions/mission-1/work",
            PreparedAtUtc = preparedAt,
        });
        await store.MarkDeletedAsync("mission-1", deletedAt);

        var record = await store.GetAsync("mission-1");

        Assert.NotNull(record);
        Assert.Equal(preparedAt, record!.PreparedAtUtc);
        Assert.Equal(deletedAt, record.DeletedAtUtc);
    }
}
