using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WorkAgents.Core.Abstractions;
using WorkAgents.Core.Missions;
using WorkAgents.Orchestration;

namespace WorkAgents.Infrastructure.Execution;

/// <summary>Resolves incomplete mission rows after a host restart.</summary>
public sealed class MissionRecoveryHostedService : BackgroundService
{
    private readonly IMissionStore _missions;
    private readonly ICheckpointStore _checkpoints;
    private readonly ILogger<MissionRecoveryHostedService> _logger;

    public MissionRecoveryHostedService(IMissionStore missions, ICheckpointStore checkpoints, ILogger<MissionRecoveryHostedService> logger)
    {
        _missions = missions;
        _checkpoints = checkpoints;
        _logger = logger;
    }

    public async Task RecoverAsync(CancellationToken ct = default)
    {
        var missions = await _missions.ListAsync(new MissionQuery
        {
            Statuses = [MissionStatus.Running, MissionStatus.Paused, MissionStatus.AwaitingApproval],
            Limit = 500,
        }, ct);
        foreach (var mission in missions)
        {
            var checkpoint = await _checkpoints.GetLatestAsync(mission.MissionId, ct);
            if (checkpoint is null)
            {
                await _missions.SetStatusAsync(
                    mission.MissionId,
                    MissionStatus.Failed,
                    MissionOutcome.Failed,
                    MissionStopReason.NoCheckpoint,
                    "Mission had no restart checkpoint.",
                    ct);
                continue;
            }
            _logger.LogInformation("mission {MissionId} has recovery checkpoint {CheckpointId}", mission.MissionId, checkpoint.CheckpointId);
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await RecoverAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "mission recovery failed");
        }
    }
}
