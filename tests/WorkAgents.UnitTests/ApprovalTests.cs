using WorkAgents.Core;
using WorkAgents.Core.Abstractions;
using WorkAgents.Infrastructure.Approvals;
using WorkAgents.Infrastructure.Stores;
using Microsoft.Data.Sqlite;

namespace WorkAgents.UnitTests;

public sealed class ApprovalTests
{
    [Fact]
    public void Create_sets_pending_status_and_expiry()
    {
        var now = DateTimeOffset.Parse("2026-07-20T10:00:00+00:00");

        var request = ApprovalRequest.Create(
            "run-1",
            "shell",
            "git status",
            TimeSpan.FromMinutes(5),
            now,
            "approval-1");

        Assert.Equal(ApprovalStatus.Pending, request.Status);
        Assert.Equal(now.AddMinutes(5), request.ExpiresAt);
        Assert.True(request.IsExpired(request.ExpiresAt));
    }

    [Fact]
    public async Task Sqlite_store_persists_and_accepts_only_one_decision()
    {
        var database = NewDatabasePath();
        try
        {
            var store = new SqliteApprovalStore(database);
            var request = ApprovalRequest.Create(
                "run-1",
                "shell",
                "git status",
                TimeSpan.FromMinutes(5),
                approvalId: "approval-1");

            await store.CreateAsync(request);

            var pending = await store.ListPendingAsync("run-1");
            Assert.Single(pending);
            Assert.Equal(request, pending[0]);

            Assert.True(await store.TryDecideAsync(
                request.ApprovalId,
                ApprovalStatus.Approved,
                "operator"));
            Assert.False(await store.TryDecideAsync(
                request.ApprovalId,
                ApprovalStatus.Rejected,
                "operator"));

            var decided = await store.GetAsync(request.ApprovalId);
            Assert.NotNull(decided);
            Assert.Equal(ApprovalStatus.Approved, decided.Status);
            Assert.Equal("operator", decided.DecidedBy);
        }
        finally
        {
            DeleteDatabase(database);
        }
    }

    [Fact]
    public async Task Approval_service_resumes_run_after_approval()
    {
        var database = NewDatabasePath();
        try
        {
            var (runStore, approvalStore, service, runId) = await CreateRunningRunAsync(database);
            var waiting = service.RequestAsync(
                runId,
                "shell",
                "git status",
                TimeSpan.FromSeconds(5));
            var request = await WaitForPendingAsync(approvalStore, runId);

            Assert.True(await service.DecideAsync(request.ApprovalId, ApprovalStatus.Approved, "operator"));
            var decision = await waiting;

            Assert.Equal(ApprovalStatus.Approved, decision.Status);
            Assert.Equal(RunStatus.Running, await runStore.GetStatusAsync(runId));
        }
        finally
        {
            DeleteDatabase(database);
        }
    }

    [Fact]
    public async Task Approval_service_aborts_run_after_rejection()
    {
        var database = NewDatabasePath();
        try
        {
            var (runStore, approvalStore, service, runId) = await CreateRunningRunAsync(database);
            var waiting = service.RequestAsync(
                runId,
                "shell",
                "git push origin main",
                TimeSpan.FromSeconds(5));
            var request = await WaitForPendingAsync(approvalStore, runId);

            Assert.True(await service.DecideAsync(
                request.ApprovalId,
                ApprovalStatus.Rejected,
                "operator",
                "push is not allowed"));
            var decision = await waiting;

            Assert.Equal(ApprovalStatus.Rejected, decision.Status);
            Assert.Equal(RunStatus.Aborted, await runStore.GetStatusAsync(runId));
            var run = await runStore.GetAsync(runId);
            Assert.Equal("push is not allowed", run?.Error);
        }
        finally
        {
            DeleteDatabase(database);
        }
    }

    [Fact]
    public async Task Approval_service_rejects_and_aborts_after_timeout()
    {
        var database = NewDatabasePath();
        try
        {
            var (runStore, approvalStore, service, runId) = await CreateRunningRunAsync(database);
            var decision = await service.RequestAsync(
                runId,
                "shell",
                "dangerous command",
                TimeSpan.FromMilliseconds(30));

            Assert.Equal(ApprovalStatus.Rejected, decision.Status);
            Assert.Equal("system", decision.DecidedBy);
            Assert.Equal(RunStatus.Aborted, await runStore.GetStatusAsync(runId));
            Assert.Empty(await approvalStore.ListPendingAsync(runId));
        }
        finally
        {
            DeleteDatabase(database);
        }
    }

    [Fact]
    public async Task Mission_approval_preserves_mission_dimensions_without_touching_legacy_run()
    {
        var database = NewDatabasePath();
        try
        {
            var runStore = new SqliteRunStore(database);
            var approvalStore = new SqliteApprovalStore(database);
            var service = new ApprovalService(approvalStore, runStore);
            var waiting = service.RequestMissionAsync(
                "mission-1",
                "instance-1",
                "shell",
                "safe summary",
                TimeSpan.FromSeconds(5),
                nodeRunId: "node-1",
                iterationId: "iteration-1");
            var request = await WaitForPendingAsync(approvalStore, "mission:mission-1");

            Assert.Equal("mission-1", request.MissionId);
            Assert.Equal("instance-1", request.AgentInstanceId);
            Assert.Equal("node-1", request.NodeRunId);
            Assert.Equal("iteration-1", request.IterationId);
            Assert.True(await service.DecideAsync(request.ApprovalId, ApprovalStatus.Approved, "operator"));
            Assert.Equal(ApprovalStatus.Approved, (await waiting).Status);
        }
        finally
        {
            DeleteDatabase(database);
        }
    }

    private static async Task<(SqliteRunStore RunStore, SqliteApprovalStore ApprovalStore, ApprovalService Service, string RunId)>
        CreateRunningRunAsync(string database)
    {
        var runStore = new SqliteRunStore(database);
        var approvalStore = new SqliteApprovalStore(database);
        var runId = Guid.NewGuid().ToString("N");
        await runStore.CreateAsync(new RunRecord
        {
            RunId = runId,
            AgentName = "repo-agent",
            UserMessage = "inspect repository",
        });
        Assert.True(await runStore.TrySetStatusAsync(runId, RunStatus.Queued, RunStatus.Running));
        return (runStore, approvalStore, new ApprovalService(approvalStore, runStore), runId);
    }

    private static async Task<ApprovalRequest> WaitForPendingAsync(
        IApprovalStore store,
        string runId)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            var pending = await store.ListPendingAsync(runId);
            if (pending.Count > 0)
            {
                return pending[0];
            }

            await Task.Delay(10);
        }

        throw new TimeoutException("Approval request was not created.");
    }

    private static string NewDatabasePath()
        => Path.Combine(
            Path.GetTempPath(),
            "work-agents-tests",
            Guid.NewGuid().ToString("N"),
            "state.db");

    private static void DeleteDatabase(string database)
    {
        var directory = Path.GetDirectoryName(database);
        if (!string.IsNullOrEmpty(directory) && Directory.Exists(directory))
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(directory, recursive: true);
        }
    }
}
