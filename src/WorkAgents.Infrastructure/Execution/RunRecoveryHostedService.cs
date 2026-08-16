using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WorkAgents.Core;
using WorkAgents.Core.Abstractions;

namespace WorkAgents.Infrastructure.Execution;

/// <summary>
/// Host起動時にSQLiteへ永続化されたRunを点検する(5.6)。プロセス内 <c>Channel&lt;T&gt;</c> は
/// Host再起動で失われるため、<c>Queued</c> のまま取り残されたRunをキューへ再投入し、
/// 前回プロセスの実行中に中断された(<c>Running</c>/<c>AwaitingApproval</c>)Runは安全に
/// 再開できないため <c>Aborted</c> として確定させる。<see cref="RunBackgroundService"/> より
/// 先に(DI登録順で)起動することで、キュー購読が始まる前に再投入を完了させる。
/// </summary>
public sealed class RunRecoveryHostedService : IHostedService
{
    private readonly IRunStore _store;
    private readonly IRunQueue _queue;
    private readonly ILogger<RunRecoveryHostedService> _logger;

    public RunRecoveryHostedService(IRunStore store, IRunQueue queue, ILogger<RunRecoveryHostedService> logger)
    {
        _store = store;
        _queue = queue;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var runs = await _store.ListAsync(cancellationToken);
        foreach (var run in runs)
        {
            if (run.Status == RunStatus.Queued)
            {
                await _queue.EnqueueAsync(run.RunId, cancellationToken);
                _logger.LogInformation("recovered queued run on startup: {RunId}", run.RunId);
            }
            else if (run.Status is RunStatus.Running or RunStatus.AwaitingApproval)
            {
                await _store.CompleteAsync(
                    run.RunId,
                    RunStatus.Aborted,
                    error: "Host restarted while this run was in progress.",
                    ct: cancellationToken);
                _logger.LogWarning(
                    "aborted orphaned run on startup: {RunId} previousStatus={Status}",
                    run.RunId,
                    run.Status);
            }
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
