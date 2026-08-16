using System.Diagnostics;
using System.Text.Json;
using WorkAgents.Core.Abstractions;
using WorkAgents.Core.Loops;
using WorkAgents.Orchestration.Budgets;
using WorkAgents.Orchestration.Checkpoints;

namespace WorkAgents.Orchestration.Loops;

public sealed record LoopExecutionRequest
{
    public required string MissionId { get; init; }

    public required string NodeRunId { get; init; }

    public required string AgentName { get; init; }

    public required string InitialInput { get; init; }

    public string? WorkingDirectory { get; init; }

    public int MaxIterations { get; init; } = 10;

    public double? CostLimitUsd { get; init; }

    public int? TimeLimitSeconds { get; init; }

    public double ScoreThreshold { get; init; } = 1.0;

    public EvaluatorKind EvaluatorKind { get; init; } = EvaluatorKind.Deterministic;

    public string EvaluatorRef { get; init; } = "loop";

    public Func<int, string, CancellationToken, Task<LoopIterationEvaluation>>? EvaluateIteration { get; init; }
}

public sealed record LoopIterationEvaluation(
    double Score,
    string? Output = null,
    string? Notes = null,
    IReadOnlyList<EvaluationMetric>? Metrics = null);

public sealed record LoopExecutionResult(
    LoopRun LoopRun,
    IReadOnlyList<Iteration> Iterations,
    string? BestOutput);

public sealed record IterationEvaluatedEvent(
    string MissionId,
    string LoopRunId,
    string IterationId,
    int IterationNo,
    double Score,
    bool Passed,
    IReadOnlyList<EvaluationMetric> Metrics,
    string? Notes,
    double CostUsd);

public sealed record BudgetUpdatedEvent(string MissionId, string LoopRunId, int IterationsUsed, double CostUsedUsd);

/// <summary>Executes bounded iterations and persists every boundary and evaluation.</summary>
public sealed class LoopExecutor
{
    private readonly IAgentInvoker _invoker;
    private readonly ILoopStore? _store;
    private readonly BudgetLedger _ledger;
    private readonly Evaluator _evaluator;
    private readonly CheckpointManager? _checkpoints;

    public event Func<IterationEvaluatedEvent, Task>? IterationEvaluated;

    public event Func<BudgetUpdatedEvent, Task>? BudgetUpdated;

    public LoopExecutor(
        IAgentInvoker invoker,
        ILoopStore? store = null,
        BudgetLedger? ledger = null,
        Evaluator? evaluator = null,
        CheckpointManager? checkpoints = null)
    {
        ArgumentNullException.ThrowIfNull(invoker);
        _invoker = invoker;
        _store = store;
        _ledger = ledger ?? new BudgetLedger();
        _evaluator = evaluator ?? new Evaluator();
        _checkpoints = checkpoints;
    }

    public async Task<LoopExecutionResult> ExecuteAsync(LoopExecutionRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var maxIterations = Math.Clamp(request.MaxIterations <= 0 ? 10 : request.MaxIterations, 1, 100);
        var loopRun = new LoopRun
        {
            LoopRunId = Guid.NewGuid().ToString("N"),
            MissionId = request.MissionId,
            NodeRunId = request.NodeRunId,
            MaxIterations = maxIterations,
            CostLimitUsd = request.CostLimitUsd,
            TimeLimitSeconds = request.TimeLimitSeconds,
            ScoreThreshold = request.ScoreThreshold,
        };
        if (_store is not null)
        {
            await _store.CreateLoopRunAsync(loopRun, ct);
        }

        var iterations = new List<Iteration>();
        var input = request.InitialInput;
        var bestScore = double.MinValue;
        string? bestOutput = null;
        var stopwatch = Stopwatch.StartNew();
        var stopReason = LoopStopReason.MaxIterations;

        for (var iterationNo = 1; iterationNo <= maxIterations; iterationNo++)
        {
            ct.ThrowIfCancellationRequested();
            if (request.TimeLimitSeconds.HasValue && stopwatch.Elapsed.TotalSeconds >= request.TimeLimitSeconds.Value)
            {
                stopReason = LoopStopReason.TimeLimit;
                break;
            }
            var iteration = new Iteration
            {
                IterationId = Guid.NewGuid().ToString("N"),
                LoopRunId = loopRun.LoopRunId,
                IterationNo = iterationNo,
                InputJson = input,
            };
            if (_store is not null)
            {
                await _store.CreateIterationAsync(iteration, ct);
            }

            var turnBudget = new WorkAgents.Core.Missions.Budget
            {
                MissionId = request.MissionId,
                CostLimitUsd = request.CostLimitUsd,
                TimeLimitSeconds = request.TimeLimitSeconds,
                CostUsedUsd = 0,
            };
            _ledger.EnsureCanStartTurn(turnBudget);
            var started = stopwatch.Elapsed;
            var invocation = await _invoker.InvokeAsync(new AgentInvocation
            {
                AgentName = request.AgentName,
                Context = input,
                WorkingDirectory = request.WorkingDirectory,
                MissionId = request.MissionId,
                ThreadId = $"loop:{loopRun.LoopRunId}",
            }, ct);
            var duration = stopwatch.Elapsed - started;
            var evaluation = request.EvaluateIteration is null
                ? new LoopIterationEvaluation(1.0, invocation.Utterance, Metrics: Array.Empty<EvaluationMetric>())
                : await request.EvaluateIteration(iterationNo, invocation.Utterance, ct);
            var metrics = evaluation.Metrics ?? Array.Empty<EvaluationMetric>();
            var evalResult = _evaluator.Evaluate(new EvaluationInput
            {
                IterationId = iteration.IterationId,
                EvaluatorRef = request.EvaluatorRef,
                EvaluatorKind = request.EvaluatorKind,
                Score = evaluation.Score,
                Notes = evaluation.Notes,
                Metrics = metrics,
                ScoreThreshold = request.ScoreThreshold,
            });
            await NotifyAsync(IterationEvaluated, new IterationEvaluatedEvent(
                request.MissionId,
                loopRun.LoopRunId,
                iteration.IterationId,
                iterationNo,
                evalResult.Evaluation.Score,
                evalResult.Evaluation.Passed,
                evalResult.Metrics,
                evalResult.Evaluation.Notes,
                iteration.CostUsd));
            var completed = iteration with
            {
                State = evalResult.Evaluation.Passed ? IterationState.Succeeded : IterationState.Failed,
                OutputJson = evaluation.Output ?? invocation.Utterance,
                DurationMs = (long)Math.Max(0, duration.TotalMilliseconds),
                CompletedAt = DateTimeOffset.UtcNow,
            };
            iterations.Add(completed);
            if (_store is not null)
            {
                await _store.CompleteIterationAsync(
                    completed.IterationId,
                    completed.State,
                    completed.OutputJson,
                    completed.CostUsd,
                    completed.Tokens,
                    completed.DurationMs,
                    ct);
                await _store.AddEvaluationAsync(
                    evalResult.Evaluation,
                    evalResult.Metrics.Select(metric => metric with { EvaluationId = evalResult.Evaluation.EvaluationId }).ToArray(),
                    ct);
            }
            if (_checkpoints is not null)
            {
                await _checkpoints.SaveAsync(
                    request.MissionId,
                    WorkAgents.Core.Missions.CheckpointBoundaryKind.Iteration,
                    JsonSerializer.Serialize(new { loopRunId = loopRun.LoopRunId, iterationNo, output = completed.OutputJson }),
                    0,
                    workspacePath: request.WorkingDirectory,
                    iterationId: completed.IterationId,
                    ct: ct);
            }
            await NotifyAsync(BudgetUpdated, new BudgetUpdatedEvent(request.MissionId, loopRun.LoopRunId, iterationNo, iterations.Sum(item => item.CostUsd)));

            if (evaluation.Score > bestScore)
            {
                bestScore = evaluation.Score;
                bestOutput = completed.OutputJson;
            }
            var decision = LoopStopConditionEvaluator.Evaluate(
                iterationNo,
                maxIterations,
                evalResult.Evaluation.Passed,
                costUsedUsd: completed.CostUsd,
                costLimitUsd: request.CostLimitUsd,
                elapsed: stopwatch.Elapsed,
                timeLimit: request.TimeLimitSeconds is null ? null : TimeSpan.FromSeconds(request.TimeLimitSeconds.Value));
            if (decision.ShouldStop)
            {
                stopReason = decision.Reason!.Value;
                break;
            }
            input = completed.OutputJson ?? invocation.Utterance;
        }

        stopwatch.Stop();
        loopRun = loopRun with
        {
            StopReason = stopReason,
            BestIterationId = iterations.OrderByDescending(iteration => iteration.OutputJson == bestOutput).ThenByDescending(iteration => iteration.IterationNo).FirstOrDefault()?.IterationId,
            CompletedAt = DateTimeOffset.UtcNow,
        };
        if (_store is not null)
        {
            await _store.CompleteLoopRunAsync(loopRun.LoopRunId, stopReason, loopRun.BestIterationId, ct);
        }
        return new LoopExecutionResult(loopRun, iterations, bestOutput);
    }

    private static async Task NotifyAsync<T>(Func<T, Task>? handlers, T value)
    {
        if (handlers is null) return;
        foreach (var handler in handlers.GetInvocationList().Cast<Func<T, Task>>())
        {
            try { await handler(value); } catch { }
        }
    }
}
