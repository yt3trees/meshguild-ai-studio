using Microsoft.Extensions.Logging.Abstractions;
using WorkAgents.Core;
using WorkAgents.Core.Missions;
using WorkAgents.Infrastructure.Execution;
using WorkAgents.Infrastructure.Stores;

namespace WorkAgents.UnitTests.Execution;

public sealed class WorkspaceRetentionPlannerTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 11, 0, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan RetentionPeriod = TimeSpan.FromDays(7);

    [Fact]
    public void SelectDirectoriesToDelete_ExcludesRunningRuns()
    {
        var candidates = new[]
        {
            new WorkspaceDirectoryCandidate(
                @"C:\work-agents\runs\run-running",
                CreateRun("run-running", RunStatus.Running, completedAt: null),
                Now - TimeSpan.FromDays(30)),
        };

        var result = WorkspaceRetentionPlanner.SelectDirectoriesToDelete(candidates, RetentionPeriod, Now);

        Assert.Empty(result);
    }

    [Fact]
    public void SelectDirectoriesToDelete_ExcludesWithinRetentionPeriod()
    {
        var candidates = new[]
        {
            new WorkspaceDirectoryCandidate(
                @"C:\work-agents\runs\run-recent",
                CreateRun("run-recent", RunStatus.Succeeded, completedAt: Now - TimeSpan.FromDays(1)),
                Now - TimeSpan.FromDays(1)),
        };

        var result = WorkspaceRetentionPlanner.SelectDirectoriesToDelete(candidates, RetentionPeriod, Now);

        Assert.Empty(result);
    }

    [Fact]
    public void SelectDirectoriesToDelete_IncludesCompletedPastRetentionPeriod()
    {
        var candidates = new[]
        {
            new WorkspaceDirectoryCandidate(
                @"C:\work-agents\runs\run-old",
                CreateRun("run-old", RunStatus.Succeeded, completedAt: Now - TimeSpan.FromDays(10)),
                Now - TimeSpan.FromDays(10)),
        };

        var result = WorkspaceRetentionPlanner.SelectDirectoriesToDelete(candidates, RetentionPeriod, Now);

        Assert.Equal([@"C:\work-agents\runs\run-old"], result);
    }

    [Fact]
    public void SelectDirectoriesToDelete_FallsBackToLastWriteTimeWhenCompletedAtMissing()
    {
        var candidates = new[]
        {
            new WorkspaceDirectoryCandidate(
                @"C:\work-agents\runs\run-no-completed-at",
                CreateRun("run-no-completed-at", RunStatus.Aborted, completedAt: null),
                Now - TimeSpan.FromDays(10)),
        };

        var result = WorkspaceRetentionPlanner.SelectDirectoriesToDelete(candidates, RetentionPeriod, Now);

        Assert.Equal([@"C:\work-agents\runs\run-no-completed-at"], result);
    }

    [Fact]
    public void SelectDirectoriesToDelete_SkipsDirectoriesWithoutMatchingRun()
    {
        var candidates = new[]
        {
            new WorkspaceDirectoryCandidate(
                @"C:\work-agents\runs\repo-agent",
                Run: null,
                Now - TimeSpan.FromDays(30)),
        };

        var result = WorkspaceRetentionPlanner.SelectDirectoriesToDelete(candidates, RetentionPeriod, Now);

        Assert.Empty(result);
    }

    private static RunRecord CreateRun(string runId, RunStatus status, DateTimeOffset? completedAt) => new()
    {
        RunId = runId,
        AgentName = "test-agent",
        UserMessage = "test",
        Status = status,
        CompletedAt = completedAt,
    };
}

public sealed class WorkspaceRetentionBackgroundServiceTests
{
    [Fact]
    public async Task TickAsync_DeletesEligibleMissionWorkspaceAndProtectsActiveMission()
    {
        var databasePath = CreateDatabasePath();
        var workspaceRoot = CreateWorkspaceRoot();
        try
        {
            var runs = new SqliteRunStore(databasePath);
            var missions = new SqliteMissionStore(databasePath);
            var workspaces = new SqliteMissionWorkspaceStore(databasePath);
            await CreateMissionAsync(missions, workspaces, workspaceRoot, "mission-active", MissionStatus.Running);
            await CreateMissionAsync(missions, workspaces, workspaceRoot, "mission-old", MissionStatus.Succeeded);

            var service = new WorkspaceRetentionBackgroundService(
                runs,
                workspaceRoot,
                new WorkspaceRetentionOptions { RetentionPeriod = TimeSpan.Zero },
                new WorkspaceUsageSnapshot(),
                NullLogger<WorkspaceRetentionBackgroundService>.Instance,
                missions,
                workspaces);

            var result = await service.TickAsync(DateTimeOffset.UtcNow.AddMinutes(1));
            var oldRecord = await workspaces.GetAsync("mission-old");

            Assert.True(Directory.Exists(Path.Combine(workspaceRoot, "missions", "mission-active", "work")));
            Assert.False(Directory.Exists(Path.Combine(workspaceRoot, "missions", "mission-old", "work")));
            Assert.Equal(1, result.DeletedCount);
            Assert.NotNull(oldRecord?.DeletedAtUtc);
        }
        finally
        {
            DeleteDatabaseDirectory(databasePath);
            DeleteWorkspaceRoot(workspaceRoot);
        }
    }
    [Fact]
    public async Task TickAsync_DeletesTerminalRunsPastRetention_AndSkipsRunningOnes()
    {
        var databasePath = CreateDatabasePath();
        var workspaceRoot = CreateWorkspaceRoot();
        try
        {
            var store = new SqliteRunStore(databasePath);

            await CreateRunAsync(store, workspaceRoot, "run-running", complete: false);
            await CreateRunAsync(store, workspaceRoot, "run-succeeded", complete: true);

            var options = new WorkspaceRetentionOptions { RetentionPeriod = TimeSpan.Zero };
            var snapshot = new WorkspaceUsageSnapshot();
            var service = new WorkspaceRetentionBackgroundService(
                store,
                workspaceRoot,
                options,
                snapshot,
                NullLogger<WorkspaceRetentionBackgroundService>.Instance);

            // RetentionPeriod=Zeroなので、CompleteAsync呼び出し(現在時刻)より少し先の時刻を渡せば
            // 実際の待機なしに「保持期限を過ぎた」状態を再現できる。
            var result = await service.TickAsync(DateTimeOffset.UtcNow.AddMinutes(1), CancellationToken.None);

            Assert.True(Directory.Exists(Path.Combine(workspaceRoot, "run-running")));
            Assert.False(Directory.Exists(Path.Combine(workspaceRoot, "run-succeeded")));
            Assert.Equal(1, result.DeletedCount);
            Assert.Equal(0, result.FailedCount);
            Assert.Equal(2, result.EvaluatedCount);
            Assert.Equal(result, snapshot.LastSweep);
        }
        finally
        {
            DeleteDatabaseDirectory(databasePath);
            DeleteWorkspaceRoot(workspaceRoot);
        }
    }

    [Fact]
    public async Task TickAsync_KeepsTerminalRunsWithinRetentionPeriod()
    {
        var databasePath = CreateDatabasePath();
        var workspaceRoot = CreateWorkspaceRoot();
        try
        {
            var store = new SqliteRunStore(databasePath);
            await CreateRunAsync(store, workspaceRoot, "run-succeeded", complete: true);

            var options = new WorkspaceRetentionOptions { RetentionPeriod = TimeSpan.FromDays(7) };
            var service = new WorkspaceRetentionBackgroundService(
                store,
                workspaceRoot,
                options,
                new WorkspaceUsageSnapshot(),
                NullLogger<WorkspaceRetentionBackgroundService>.Instance);

            var result = await service.TickAsync(DateTimeOffset.UtcNow, CancellationToken.None);

            Assert.True(Directory.Exists(Path.Combine(workspaceRoot, "run-succeeded")));
            Assert.Equal(0, result.DeletedCount);
        }
        finally
        {
            DeleteDatabaseDirectory(databasePath);
            DeleteWorkspaceRoot(workspaceRoot);
        }
    }

    [Fact]
    public async Task TickAsync_SkipsDirectoriesWithoutMatchingRunRecord()
    {
        var databasePath = CreateDatabasePath();
        var workspaceRoot = CreateWorkspaceRoot();
        try
        {
            var store = new SqliteRunStore(databasePath);
            var unmatched = Path.Combine(workspaceRoot, "repo-agent");
            Directory.CreateDirectory(unmatched);
            Directory.SetLastWriteTimeUtc(unmatched, DateTimeOffset.UtcNow.AddDays(-30).UtcDateTime);

            var options = new WorkspaceRetentionOptions { RetentionPeriod = TimeSpan.Zero };
            var service = new WorkspaceRetentionBackgroundService(
                store,
                workspaceRoot,
                options,
                new WorkspaceUsageSnapshot(),
                NullLogger<WorkspaceRetentionBackgroundService>.Instance);

            var result = await service.TickAsync(DateTimeOffset.UtcNow, CancellationToken.None);

            Assert.True(Directory.Exists(unmatched));
            Assert.Equal(0, result.DeletedCount);
        }
        finally
        {
            DeleteDatabaseDirectory(databasePath);
            DeleteWorkspaceRoot(workspaceRoot);
        }
    }

    [Fact]
    public async Task TickAsync_RecordsFailureWhenDeletionFailsAndContinues()
    {
        var databasePath = CreateDatabasePath();
        var workspaceRoot = CreateWorkspaceRoot();
        try
        {
            var store = new SqliteRunStore(databasePath);
            await CreateRunAsync(store, workspaceRoot, "run-locked", complete: true);
            await CreateRunAsync(store, workspaceRoot, "run-succeeded", complete: true);

            var lockedFilePath = Path.Combine(workspaceRoot, "run-locked", "locked.txt");
            await using var lockedStream = new FileStream(lockedFilePath, FileMode.Create, FileAccess.ReadWrite, FileShare.None);

            var options = new WorkspaceRetentionOptions { RetentionPeriod = TimeSpan.Zero };
            var service = new WorkspaceRetentionBackgroundService(
                store,
                workspaceRoot,
                options,
                new WorkspaceUsageSnapshot(),
                NullLogger<WorkspaceRetentionBackgroundService>.Instance);

            var result = await service.TickAsync(DateTimeOffset.UtcNow.AddMinutes(1), CancellationToken.None);

            Assert.True(Directory.Exists(Path.Combine(workspaceRoot, "run-locked")));
            Assert.False(Directory.Exists(Path.Combine(workspaceRoot, "run-succeeded")));
            Assert.Equal(1, result.DeletedCount);
            Assert.Equal(1, result.FailedCount);
        }
        finally
        {
            DeleteDatabaseDirectory(databasePath);
            DeleteWorkspaceRoot(workspaceRoot);
        }
    }

    private static async Task CreateRunAsync(SqliteRunStore store, string workspaceRoot, string runId, bool complete)
    {
        await store.CreateAsync(new RunRecord
        {
            RunId = runId,
            AgentName = "test-agent",
            UserMessage = "test",
        });
        await store.TrySetStatusAsync(runId, RunStatus.Queued, RunStatus.Running);
        if (complete)
        {
            await store.CompleteAsync(runId, RunStatus.Succeeded, result: "ok");
        }

        var dir = Path.Combine(workspaceRoot, runId);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "file.txt"), "content");
    }

    private static async Task CreateMissionAsync(
        SqliteMissionStore store,
        SqliteMissionWorkspaceStore workspaces,
        string workspaceRoot,
        string missionId,
        MissionStatus status)
    {
        await store.CreateAsync(new Mission
        {
            MissionId = missionId,
            Goal = "test",
            TargetKind = MissionTargetKind.Team,
            TargetName = "team",
        });
        await store.SetStatusAsync(missionId, MissionStatus.Running);
        if (status != MissionStatus.Running)
        {
            await store.SetStatusAsync(missionId, status, status switch
            {
                MissionStatus.Succeeded => MissionOutcome.Succeeded,
                MissionStatus.Failed => MissionOutcome.Failed,
                MissionStatus.Aborted => MissionOutcome.Aborted,
                MissionStatus.NotConverged => MissionOutcome.NotConverged,
                _ => null,
            });
        }

        var directory = Path.Combine(workspaceRoot, "missions", missionId, "work");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "file.txt"), "content");
        await workspaces.RecordPreparedAsync(new MissionWorkspaceRecord
        {
            MissionId = missionId,
            WorkspaceKey = $"missions/{missionId}/work",
            PreparedAtUtc = DateTimeOffset.UtcNow.AddDays(-30),
        });
    }

    private static string CreateDatabasePath()
        => Path.Combine(Path.GetTempPath(), "work-agents-tests", $"{Guid.NewGuid():N}", "runs.db");

    private static string CreateWorkspaceRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "work-agents-tests", $"{Guid.NewGuid():N}", "workspace");
        Directory.CreateDirectory(root);
        return root;
    }

    private static void DeleteDatabaseDirectory(string databasePath)
    {
        var directory = Path.GetDirectoryName(databasePath);
        if (directory is not null && Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static void DeleteWorkspaceRoot(string workspaceRoot)
    {
        if (Directory.Exists(workspaceRoot))
        {
            Directory.Delete(workspaceRoot, recursive: true);
        }
    }
}
