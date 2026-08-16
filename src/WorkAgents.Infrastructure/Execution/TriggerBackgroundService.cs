using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WorkAgents.Core.Abstractions;
using WorkAgents.Core.Missions;
using WorkAgents.Core.Triggers;
using WorkAgents.Orchestration;

namespace WorkAgents.Infrastructure.Execution;

/// <summary>Fires persisted schedule and interval triggers and records overlap decisions.</summary>
public sealed class TriggerBackgroundService : BackgroundService
{
    private readonly ITriggerStore _triggers;
    private readonly IMissionStore _missions;
    private readonly MissionEngine _engine;
    private readonly ILogger<TriggerBackgroundService> _logger;

    public TriggerBackgroundService(ITriggerStore triggers, IMissionStore missions, MissionEngine engine, ILogger<TriggerBackgroundService> logger)
    {
        _triggers = triggers;
        _missions = missions;
        _engine = engine;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                await TickAsync(DateTimeOffset.UtcNow, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    public async Task TickAsync(DateTimeOffset now, CancellationToken ct = default)
    {
        var due = (await _triggers.ListAsync(ct))
            .Where(trigger => trigger.Enabled
                && trigger.Kind is (TriggerKind.Schedule or TriggerKind.Interval)
                && trigger.NextRunAt is not null
                && trigger.NextRunAt <= now)
            .OrderBy(trigger => trigger.NextRunAt)
            .ToArray();
        foreach (var trigger in due)
        {
            try
            {
                await FireAsync(trigger, now, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "trigger fire failed trigger={Trigger}", trigger.Name);
            }
        }
    }

    private async Task FireAsync(TriggerDefinition trigger, DateTimeOffset now, CancellationToken ct)
    {
        var active = (await _missions.ListAsync(new MissionQuery { Limit = 500 }, ct))
            .Any(mission => mission.TriggerId == trigger.TriggerId
                && mission.Status is not (MissionStatus.Succeeded or MissionStatus.NotConverged or MissionStatus.Failed or MissionStatus.Aborted));
        var decision = TriggerDecision.Started;
        var reason = "trigger due";
        string? missionId = null;
        if (active && trigger.OverlapPolicy == OverlapPolicy.Skip)
        {
            decision = TriggerDecision.Skipped;
            reason = "overlap policy skip";
        }
        else
        {
            if (active && trigger.OverlapPolicy == OverlapPolicy.Queue)
            {
                decision = TriggerDecision.Queued;
                reason = "overlap policy queue";
            }
            else if (active && trigger.OverlapPolicy == OverlapPolicy.Parallel)
            {
                decision = TriggerDecision.Parallel;
                reason = "overlap policy parallel";
            }
            missionId = Guid.NewGuid().ToString("N");
            var targetKind = Enum.TryParse<MissionTargetKind>(trigger.TargetKind, true, out var parsed) ? parsed : MissionTargetKind.Team;
            var created = await _engine.SubmitAsync(new Mission
            {
                MissionId = missionId,
                Goal = trigger.Input,
                TargetKind = targetKind,
                TargetName = trigger.TargetName,
                TeamName = targetKind == MissionTargetKind.Team ? trigger.TargetName : null,
                TriggerId = trigger.TriggerId,
                TriggerKind = trigger.Kind switch
                {
                    TriggerKind.Schedule => MissionTriggerKind.Schedule,
                    TriggerKind.Interval => MissionTriggerKind.Interval,
                    TriggerKind.Event => MissionTriggerKind.Event,
                    _ => MissionTriggerKind.Manual,
                },
            }, ct);
            missionId = created.MissionId;
        }
        await _triggers.RecordFireAsync(new TriggerFire
        {
            FireId = Guid.NewGuid().ToString("N"),
            TriggerId = trigger.TriggerId,
            FiredAt = now,
            Decision = decision,
            DecisionReason = reason,
            MissionId = missionId,
        }, ct);
        var next = trigger.Kind == TriggerKind.Interval && trigger.IntervalSeconds.HasValue
            ? now.AddSeconds(trigger.IntervalSeconds.Value)
            : ScheduleCalculator.GetNextOccurrence(trigger.Cron, now, TimeZoneInfo.Local);
        await _triggers.UpdateAsync(trigger with { LastRunAt = now, NextRunAt = next, UpdatedAt = DateTimeOffset.UtcNow }, ct);
    }
}
