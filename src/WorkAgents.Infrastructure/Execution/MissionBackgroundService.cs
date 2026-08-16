using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WorkAgents.Orchestration;

namespace WorkAgents.Infrastructure.Execution;

/// <summary>
/// ミッション待機列の再走査を担うホステッドサービス (T041)。
/// 通常時の昇格は <see cref="MissionEngine.CompleteAsync"/> がミッション終了のたびに行うが、
/// 本サービスは定期的に <see cref="MissionEngine.PumpQueueAsync"/> を呼び、
/// 取りこぼし (例: プロセス起動直後にキューへ残っていた分) を拾う。ホスト停止時は安全に止まる。
/// </summary>
public sealed class MissionBackgroundService : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);

    private readonly MissionEngine _engine;
    private readonly ILogger<MissionBackgroundService> _logger;

    public MissionBackgroundService(MissionEngine engine, ILogger<MissionBackgroundService> logger)
    {
        _engine = engine;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("mission background service started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await _engine.PumpQueueAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "mission queue pump failed");
            }

            try
            {
                await Task.Delay(PollInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        _logger.LogInformation("mission background service stopped");
    }
}
