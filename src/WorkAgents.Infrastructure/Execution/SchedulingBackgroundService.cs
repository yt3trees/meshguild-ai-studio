using System.Diagnostics;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WorkAgents.Agents;
using WorkAgents.Core;
using WorkAgents.Core.Abstractions;
using WorkAgents.Infrastructure.Telemetry;

namespace WorkAgents.Infrastructure.Execution;

/// <summary>
/// Local プロファイルのスケジュール実行 BackgroundService(5.13.2)。起動時に cron を持つ workflow を
/// Schedule 行として自動 bootstrap し、PeriodicTimer で due な Schedule を拾って
/// <see cref="IRunStore"/> + <see cref="IRunQueue"/> 経路に Run を投入する。
/// </summary>
public sealed class SchedulingBackgroundService : BackgroundService
{
    private readonly IScheduleStore _scheduleStore;
    private readonly IRunStore _runStore;
    private readonly IRunQueue _runQueue;
    private readonly IWorkflowRegistry _workflowRegistry;
    private readonly ILogger<SchedulingBackgroundService> _logger;
    private readonly TimeSpan _tickInterval = TimeSpan.FromSeconds(30);
    private readonly TimeZoneInfo _timeZone;

    public SchedulingBackgroundService(
        IScheduleStore scheduleStore,
        IRunStore runStore,
        IRunQueue runQueue,
        IWorkflowRegistry workflowRegistry,
        ILogger<SchedulingBackgroundService> logger)
    {
        _scheduleStore = scheduleStore;
        _runStore = runStore;
        _runQueue = runQueue;
        _workflowRegistry = workflowRegistry;
        _logger = logger;
        // Local プロファイル固定。Azure 移行時は Schedules:TimeZone 設定から構築する。
        _timeZone = TimeZoneInfo.Local;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await BootstrapSchedulesAsync(stoppingToken);

        try
        {
            using var timer = new PeriodicTimer(_tickInterval);
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                await TickAsync(DateTimeOffset.UtcNow, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("scheduling background service stopping");
        }
    }

    private async Task BootstrapSchedulesAsync(CancellationToken ct)
    {
        var workflows = _workflowRegistry.ListWorkflows();
        foreach (var w in workflows)
        {
            if (!w.HasSchedule || string.IsNullOrWhiteSpace(w.ScheduleCron))
            {
                continue;
            }

            try
            {
                var existing = await _scheduleStore.GetAsync(w.Name, ct);
                if (existing is not null)
                {
                    continue;
                }

                var now = DateTimeOffset.UtcNow;
                var next = ScheduleCalculator.GetNextOccurrence(w.ScheduleCron, now, _timeZone);
                var def = new ScheduleDefinition
                {
                    Name = w.Name,
                    WorkflowName = w.Name,
                    Input = string.Empty,
                    Cron = w.ScheduleCron,
                    Enabled = true,
                    NextRunAt = next,
                };
                await _scheduleStore.UpsertAsync(def, ct);
                _logger.LogInformation("bootstrapped schedule '{Name}' cron='{Cron}' next={Next}",
                    w.Name, w.ScheduleCron, next);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "failed to bootstrap schedule for workflow '{Name}'", w.Name);
            }
        }
    }

    private async Task TickAsync(DateTimeOffset now, CancellationToken ct)
    {
        IReadOnlyList<ScheduleDefinition> due;
        try
        {
            due = await _scheduleStore.ListDueAsync(now, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "failed to list due schedules");
            return;
        }

        foreach (var schedule in due)
        {
            using var activity = WorkAgentsTelemetry.ActivitySource.StartActivity("workagents.schedule.tick", ActivityKind.Internal);
            activity?.SetTag("workagents.schedule.name", schedule.Name);
            activity?.SetTag("workagents.workflow.name", schedule.WorkflowName);

            try
            {
                var runId = await EnqueueRunAsync(schedule, ct);
                var next = ScheduleCalculator.GetNextOccurrence(schedule.Cron, now, _timeZone)
                    ?? DateTimeOffset.MaxValue;
                await _scheduleStore.UpdateAfterFireAsync(schedule.Name, now, next, ct);
                _logger.LogInformation("scheduled run enqueued for '{Schedule}' workflow='{Workflow}' runId={RunId} next={Next}",
                    schedule.Name, schedule.WorkflowName, runId, next);
            }
            catch (Exception ex)
            {
                activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                _logger.LogError(ex, "failed to fire schedule '{Name}'", schedule.Name);
            }
        }
    }

    private async Task<string> EnqueueRunAsync(ScheduleDefinition schedule, CancellationToken ct)
    {
        var runId = Guid.NewGuid().ToString("N");
        var threadId = Guid.NewGuid().ToString("N");
        var run = new RunRecord
        {
            RunId = runId,
            AgentName = schedule.WorkflowName,
            UserMessage = schedule.Input,
            ThreadId = threadId,
        };
        await _runStore.CreateAsync(run, ct);
        try
        {
            await _runQueue.EnqueueAsync(runId, ct);
        }
        catch
        {
            await _runStore.CompleteAsync(runId, RunStatus.Aborted, error: "Scheduled run could not be queued.", ct: CancellationToken.None);
            throw;
        }
        return runId;
    }

}