using WorkAgents.Core.Missions;
using WorkAgents.Infrastructure.Execution;
using WorkAgents.Infrastructure.Stores;
using WorkAgents.UnitTests.Support;

namespace WorkAgents.UnitTests.Orchestration;

public sealed class MissionWorkspaceIsolationTests
{
    [Fact]
    public async Task DifferentMissions_CannotSeeEachOthersSameRelativeFile()
    {
        using var paths = new MissionWorkspaceTestPaths();
        var provider = new MissionWorkspaceProvider(
            new MissionWorkspacePathResolver(paths.Root),
            new SqliteMissionWorkspaceStore(paths.DatabasePath));
        var missionA = await provider.PrepareAsync("mission-a");
        var missionB = await provider.PrepareAsync("mission-b");
        Directory.CreateDirectory(missionA);
        Directory.CreateDirectory(missionB);
        File.WriteAllText(Path.Combine(missionA, "same.txt"), "A");
        File.WriteAllText(Path.Combine(missionB, "same.txt"), "B");

        Assert.Equal("A", await File.ReadAllTextAsync(Path.Combine(provider.ResolvePath("mission-a"), "same.txt")));
        Assert.Equal("B", await File.ReadAllTextAsync(Path.Combine(provider.ResolvePath("mission-b"), "same.txt")));
        Assert.NotEqual(provider.ResolvePath("mission-a"), provider.ResolvePath("mission-b"));
    }

    [Fact]
    public async Task RepreparingMission_PreservesTheSameWorkspaceForResume()
    {
        using var paths = new MissionWorkspaceTestPaths();
        var provider = new MissionWorkspaceProvider(
            new MissionWorkspacePathResolver(paths.Root),
            new SqliteMissionWorkspaceStore(paths.DatabasePath));
        var first = await provider.PrepareAsync("mission");
        File.WriteAllText(Path.Combine(first, "before-resume.txt"), "preserved");

        var resumed = await provider.PrepareAsync("mission");

        Assert.Equal(first, resumed);
        Assert.Equal("preserved", await File.ReadAllTextAsync(Path.Combine(resumed, "before-resume.txt")));
    }
}
