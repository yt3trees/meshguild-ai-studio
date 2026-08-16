using WorkAgents.Core.Missions;
using WorkAgents.Infrastructure.Stores;

namespace WorkAgents.UnitTests.Stores;

public sealed class SqliteMissionStoreTests
{
    [Fact]
    public async Task CreateAndGet_RoundTripsMission()
    {
        var databasePath = CreateDatabasePath();
        try
        {
            var store = new SqliteMissionStore(databasePath);
            var mission = new Mission
            {
                MissionId = "m1",
                Goal = "ship feature X",
                TargetKind = MissionTargetKind.Team,
                TargetName = "feature-delivery",
                TeamName = "feature-delivery",
            };

            await store.CreateAsync(mission);
            var loaded = await store.GetAsync("m1");

            Assert.NotNull(loaded);
            Assert.Equal("ship feature X", loaded!.Goal);
            Assert.Equal(MissionStatus.Queued, loaded.Status);
        }
        finally
        {
            DeleteDatabaseDirectory(databasePath);
        }
    }

    [Fact]
    public async Task SetStatusAsync_AllowsValidTransition()
    {
        var databasePath = CreateDatabasePath();
        try
        {
            var store = new SqliteMissionStore(databasePath);
            await store.CreateAsync(new Mission
            {
                MissionId = "m1",
                Goal = "goal",
                TargetKind = MissionTargetKind.Team,
                TargetName = "team-a",
            });

            await store.SetStatusAsync("m1", MissionStatus.Running);
            var loaded = await store.GetAsync("m1");

            Assert.Equal(MissionStatus.Running, loaded!.Status);
            Assert.NotNull(loaded.StartedAt);
        }
        finally
        {
            DeleteDatabaseDirectory(databasePath);
        }
    }

    [Fact]
    public async Task SetStatusAsync_RejectsInvalidTransition()
    {
        var databasePath = CreateDatabasePath();
        try
        {
            var store = new SqliteMissionStore(databasePath);
            await store.CreateAsync(new Mission
            {
                MissionId = "m1",
                Goal = "goal",
                TargetKind = MissionTargetKind.Team,
                TargetName = "team-a",
            });

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => store.SetStatusAsync("m1", MissionStatus.Succeeded));
        }
        finally
        {
            DeleteDatabaseDirectory(databasePath);
        }
    }

    [Fact]
    public async Task UpsertAndGetBudget_RoundTrips()
    {
        var databasePath = CreateDatabasePath();
        try
        {
            var store = new SqliteMissionStore(databasePath);
            await store.UpsertBudgetAsync(new Budget
            {
                MissionId = "m1",
                CostLimitUsd = 5.0,
                MaxIterations = 10,
                CostUsedUsd = 1.2,
            });

            var budget = await store.GetBudgetAsync("m1");

            Assert.NotNull(budget);
            Assert.Equal(5.0, budget!.CostLimitUsd);
            Assert.Equal(1.2, budget.CostUsedUsd);
        }
        finally
        {
            DeleteDatabaseDirectory(databasePath);
        }
    }

    private static string CreateDatabasePath()
        => Path.Combine(Path.GetTempPath(), "work-agents-tests", $"{Guid.NewGuid():N}", "missions.db");

    private static void DeleteDatabaseDirectory(string databasePath)
    {
        var directory = Path.GetDirectoryName(databasePath);
        if (directory is not null && Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
