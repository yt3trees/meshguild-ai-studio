using System.Diagnostics;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WorkAgents.Core;
using WorkAgents.Core.Abstractions;
using WorkAgents.Infrastructure.Telemetry;

namespace WorkAgents.Infrastructure.Execution;

/// <summary>LocalのChannelからRunを取り出して実行するBackgroundService。</summary>
public sealed class RunBackgroundService : BackgroundService
{
    private readonly IRunQueue _queue;
    private readonly IRunStore _store;
    private readonly IRunExecutor _executor;
    private readonly IRunProgressPublisher _progressPublisher;
    private readonly IRunCancellationRegistry _cancellationRegistry;
    private readonly TimeSpan _runTimeout;
    private readonly ILogger<RunBackgroundService> _logger;

    public RunBackgroundService(
        IRunQueue queue,
        IRunStore store,
        IRunExecutor executor,
        IRunProgressPublisher progressPublisher,
        IRunCancellationRegistry cancellationRegistry,
        ILogger<RunBackgroundService> logger,
        TimeSpan? runTimeout = null)
    {
        _queue = queue;
        _store = store;
        _executor = executor;
        _progressPublisher = progressPublisher;
        _cancellationRegistry = cancellationRegistry;
        _runTimeout = runTimeout ?? TimeSpan.FromMinutes(20);
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var runId in _queue.ReadAllAsync(stoppingToken))
            {
                await ProcessAsync(runId, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("run background service stopping");
        }
    }

    private async Task ProcessAsync(string runId, CancellationToken stoppingToken)
    {
        var run = await _store.GetAsync(runId, stoppingToken);
        if (run is null)
        {
            _logger.LogWarning("queued run not found: {RunId}", runId);
            return;
        }

        if (run.Status != RunStatus.Queued)
        {
            _logger.LogDebug("skipping already processed run {RunId} status={Status}", runId, run.Status);
            return;
        }

        using var activity = WorkAgentsTelemetry.ActivitySource.StartActivity("workagents.run", ActivityKind.Internal);
        activity?.SetTag("workagents.run.id", run.RunId);
        activity?.SetTag("workagents.agent.name", run.AgentName);
        activity?.SetTag("workagents.run.thread_id", run.ThreadId);

        if (!await _store.TrySetStatusAsync(runId, RunStatus.Queued, RunStatus.Running, stoppingToken))
        {
            _logger.LogDebug("skipping concurrently claimed run {RunId}", runId);
            return;
        }

        run = await _store.GetAsync(runId, stoppingToken)
            ?? throw new InvalidOperationException($"Run disappeared after claiming: '{runId}'.");
        await PublishSafelyAsync(run, stoppingToken);

        using var runCts = _cancellationRegistry.Register(runId, stoppingToken);
        if (_runTimeout > TimeSpan.Zero)
        {
            runCts.CancelAfter(_runTimeout);
        }

        try
        {
            var result = await _executor.ExecuteAsync(run, runCts.Token);
            await _store.CompleteAsync(runId, RunStatus.Succeeded, result, ct: stoppingToken);
            activity?.SetTag("workagents.run.status", RunStatus.Succeeded.ToString());
            await PublishStoredRunSafelyAsync(runId, CancellationToken.None);
            _logger.LogInformation("run completed successfully: {RunId}", runId);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            activity?.SetStatus(ActivityStatusCode.Error, "Host shutdown interrupted the run.");
            activity?.SetTag("workagents.run.status", RunStatus.Aborted.ToString());
            _logger.LogInformation("run interrupted by host shutdown: {RunId}", runId);
            await CompleteAsAbortedAsync(runId, "Host shutdown interrupted the run.");
        }
        catch (OperationCanceledException) when (runCts.IsCancellationRequested)
        {
            var reason = _cancellationRegistry.WasExplicitlyCancelled(runId)
                ? "Run was cancelled by request."
                : $"Run timed out after {_runTimeout.TotalMinutes:0} minutes.";
            activity?.SetStatus(ActivityStatusCode.Error, reason);
            activity?.SetTag("workagents.run.status", RunStatus.Aborted.ToString());
            _logger.LogInformation("run aborted: {RunId} reason={Reason}", runId, reason);
            await CompleteAsAbortedAsync(runId, reason);
        }
        catch (ApprovalRejectedException ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.SetTag("workagents.run.status", RunStatus.Aborted.ToString());
            activity?.SetTag("workagents.tool.name", ex.Request.Tool);
            _logger.LogInformation(
                "run aborted after approval rejection: {RunId} tool={Tool}",
                runId,
                ex.Request.Tool);
            await PublishStoredRunSafelyAsync(runId, CancellationToken.None);
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.SetTag("workagents.run.status", RunStatus.Failed.ToString());
            activity?.AddException(ex);
            _logger.LogError(ex, "run execution failed: {RunId}", runId);
            await CompleteAsFailedAsync(runId);
        }
        finally
        {
            _cancellationRegistry.Remove(runId);
        }
    }

    private async Task PublishStoredRunSafelyAsync(string runId, CancellationToken ct)
    {
        var run = await _store.GetAsync(runId, ct);
        if (run is not null)
        {
            await PublishSafelyAsync(run, ct);
        }
    }

    private async Task PublishSafelyAsync(RunRecord run, CancellationToken ct)
    {
        try
        {
            await _progressPublisher.PublishAsync(run, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "could not publish run progress: {RunId}", run.RunId);
        }
    }

    private async Task CompleteAsAbortedAsync(string runId, string reason)
    {
        try
        {
            await _store.CompleteAsync(runId, RunStatus.Aborted, error: reason, ct: CancellationToken.None);
            await PublishStoredRunSafelyAsync(runId, CancellationToken.None);
        }
        catch (Exception completionException)
        {
            _logger.LogError(completionException, "could not persist aborted status: {RunId}", runId);
        }
    }

    private async Task CompleteAsFailedAsync(string runId)
    {
        try
        {
            await _store.CompleteAsync(
                runId,
                RunStatus.Failed,
                error: "Agent execution failed.",
                ct: CancellationToken.None);
            await PublishStoredRunSafelyAsync(runId, CancellationToken.None);
        }
        catch (Exception completionException)
        {
            _logger.LogError(completionException, "could not persist failed status: {RunId}", runId);
        }
    }
}
