using Microsoft.Extensions.Logging;
using WorkAgents.Core;
using WorkAgents.Core.Abstractions;
using WorkAgents.Orchestration.Teams;

namespace WorkAgents.Infrastructure.Approvals;

/// <summary>
/// 承認要求を永続化し、決定されるまでrunを停止する。プロセス再起動後もSQLiteの状態を再読できる。
/// </summary>
public sealed class ApprovalService : IApprovalService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(250);

    private readonly IApprovalStore _approvalStore;
    private readonly IRunStore _runStore;
    private readonly ILogger<ApprovalService>? _logger;
    private readonly MessageBus? _missionMessages;

    public event Func<ApprovalRequest, Task>? Requested;

    public event Func<ApprovalRequest, Task>? Decided;

    public ApprovalService(
        IApprovalStore approvalStore,
        IRunStore runStore,
        ILogger<ApprovalService>? logger = null,
        MessageBus? missionMessages = null)
    {
        _approvalStore = approvalStore;
        _runStore = runStore;
        _logger = logger;
        _missionMessages = missionMessages;
    }

    public async Task<ApprovalRequest> RequestAsync(
        string runId,
        string tool,
        string argsSummary,
        TimeSpan timeout,
        CancellationToken ct = default)
        => await RequestAsync(runId, tool, argsSummary, timeout, title: null, ct);

    public async Task<ApprovalRequest> RequestAsync(
        string runId,
        string tool,
        string argsSummary,
        TimeSpan timeout,
        string? title,
        CancellationToken ct = default)
    {
        var currentStatus = await _runStore.GetStatusAsync(runId, ct)
            ?? throw new KeyNotFoundException($"Run not found: '{runId}'.");
        if (currentStatus == RunStatus.Running)
        {
            if (!await _runStore.TrySetStatusAsync(runId, RunStatus.Running, RunStatus.AwaitingApproval, ct))
            {
                currentStatus = await _runStore.GetStatusAsync(runId, ct)
                    ?? throw new KeyNotFoundException($"Run not found: '{runId}'.");
            }
            else
            {
                currentStatus = RunStatus.AwaitingApproval;
            }
        }

        if (currentStatus != RunStatus.AwaitingApproval)
        {
            throw new InvalidOperationException(
                $"Run '{runId}' must be running before requesting approval; current status is {currentStatus}.");
        }

        var request = ApprovalRequest.Create(runId, tool, argsSummary, timeout, title: title);
        await _approvalStore.CreateAsync(request, ct);
        await NotifyAsync(Requested, request);
        _logger?.LogInformation(
            "approval requested run={RunId} approval={ApprovalId} tool={Tool} expires={ExpiresAt}",
            request.RunId,
            request.ApprovalId,
            request.Tool,
            request.ExpiresAt);

        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var current = await _approvalStore.GetAsync(request.ApprovalId, ct)
                ?? throw new KeyNotFoundException($"Approval request not found: '{request.ApprovalId}'.");

            if (current.Status != ApprovalStatus.Pending)
            {
                await ApplyDecisionAsync(current, ct);
                return current;
            }

            var now = DateTimeOffset.UtcNow;
            if (current.IsExpired(now))
            {
                await _approvalStore.TryDecideAsync(
                    current.ApprovalId,
                    ApprovalStatus.Rejected,
                    "system",
                    "Approval timed out.",
                    now,
                    ct);
                continue;
            }

            var remaining = current.ExpiresAt - now;
            await Task.Delay(remaining < PollInterval ? remaining : PollInterval, ct);
        }
    }

    public async Task<bool> DecideAsync(
        string approvalId,
        ApprovalStatus status,
        string decidedBy,
        string? reason = null,
        CancellationToken ct = default)
    {
        ApprovalStatusMachine.EnsureTransition(ApprovalStatus.Pending, status);
        ArgumentException.ThrowIfNullOrWhiteSpace(decidedBy);
        var decided = await _approvalStore.TryDecideAsync(approvalId, status, decidedBy, reason, ct: ct);
        if (decided)
        {
            var request = await _approvalStore.GetAsync(approvalId, ct);
            if (request is not null) await NotifyAsync(Decided, request);
        }
        return decided;
    }

    /// <summary>Creates an approval for a mission agent without changing legacy Run state.</summary>
    public async Task<ApprovalRequest> RequestMissionAsync(
        string missionId,
        string agentInstanceId,
        string tool,
        string argsSummary,
        TimeSpan timeout,
        string? title = null,
        string? nodeRunId = null,
        string? iterationId = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(missionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(agentInstanceId);
        var now = DateTimeOffset.UtcNow;
        var request = ApprovalRequest.Create(
            $"mission:{missionId}",
            tool,
            argsSummary,
            timeout,
            now,
            title: title) with
        {
            MissionId = missionId,
            AgentInstanceId = agentInstanceId,
            NodeRunId = nodeRunId,
            IterationId = iterationId,
        };
        await _approvalStore.CreateAsync(request, ct);
        await NotifyAsync(Requested, request);

        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var current = await _approvalStore.GetAsync(request.ApprovalId, ct)
                ?? throw new KeyNotFoundException($"Approval request not found: '{request.ApprovalId}'.");
            if (current.Status != ApprovalStatus.Pending)
            {
                if (current.Status == ApprovalStatus.Rejected && _missionMessages is not null && current.MissionId is not null)
                {
                    await _missionMessages.SendAsync(
                        current.MissionId,
                        WorkAgents.Core.Missions.MessageSenderKind.System,
                        WorkAgents.Core.Missions.MessageKind.Share,
                        $"Approval rejected for {current.Tool}. Reason: {current.DecisionReason ?? "not approved"}",
                        ct: ct);
                }
                return current;
            }
            var currentTime = DateTimeOffset.UtcNow;
            if (current.IsExpired(currentTime))
            {
                await _approvalStore.TryDecideAsync(
                    current.ApprovalId,
                    ApprovalStatus.Rejected,
                    "system",
                    "Approval timed out.",
                    currentTime,
                    ct);
                continue;
            }
            var remaining = current.ExpiresAt - currentTime;
            await Task.Delay(remaining < PollInterval ? remaining : PollInterval, ct);
        }
    }

    private async Task ApplyDecisionAsync(ApprovalRequest request, CancellationToken ct)
    {
        var status = await _runStore.GetStatusAsync(request.RunId, ct)
            ?? throw new KeyNotFoundException($"Run not found: '{request.RunId}'.");

        if (request.Status == ApprovalStatus.Approved)
        {
            if (status == RunStatus.AwaitingApproval)
            {
                await _runStore.TrySetStatusAsync(
                    request.RunId,
                    RunStatus.AwaitingApproval,
                    RunStatus.Running,
                    ct);
                status = await _runStore.GetStatusAsync(request.RunId, ct)
                    ?? throw new KeyNotFoundException($"Run not found: '{request.RunId}'.");
            }

            if (status != RunStatus.Running)
            {
                throw new InvalidOperationException(
                    $"Approved run '{request.RunId}' cannot resume from status {status}.");
            }

            return;
        }

        if (status == RunStatus.AwaitingApproval)
        {
            await _runStore.CompleteAsync(
                request.RunId,
                RunStatus.Aborted,
                error: request.DecisionReason ?? "Approval rejected.",
                ct: ct);
            status = RunStatus.Aborted;
        }

        if (status != RunStatus.Aborted)
        {
            throw new InvalidOperationException(
                $"Rejected run '{request.RunId}' cannot abort from status {status}.");
        }

        _logger?.LogInformation(
            "approval rejected run={RunId} approval={ApprovalId} decidedBy={DecidedBy}",
            request.RunId,
            request.ApprovalId,
            request.DecidedBy);
    }

    private static async Task NotifyAsync(Func<ApprovalRequest, Task>? handlers, ApprovalRequest request)
    {
        if (handlers is null) return;
        foreach (var handler in handlers.GetInvocationList().Cast<Func<ApprovalRequest, Task>>())
        {
            try { await handler(request); }
            catch { /* Notification failure must not change persisted approval state. */ }
        }
    }
}
