using WorkAgents.Core.Loops;

namespace WorkAgents.Core.Abstractions;

/// <summary>LoopRun / Iteration / Evaluation / EvaluationMetric の永続化抽象。</summary>
public interface ILoopStore
{
    Task CreateLoopRunAsync(LoopRun loopRun, CancellationToken ct = default);

    Task<LoopRun?> GetLoopRunAsync(string loopRunId, CancellationToken ct = default);

    Task<IReadOnlyList<LoopRun>> ListLoopRunsAsync(string missionId, CancellationToken ct = default);

    Task CompleteLoopRunAsync(
        string loopRunId,
        LoopStopReason stopReason,
        string? bestIterationId,
        CancellationToken ct = default);

    /// <summary>(loop_run_id, iteration_no) の非破棄行に対して一意。</summary>
    Task CreateIterationAsync(Iteration iteration, CancellationToken ct = default);

    Task<IReadOnlyList<Iteration>> ListIterationsAsync(string loopRunId, bool includeDiscarded = false, CancellationToken ct = default);

    Task CompleteIterationAsync(string iterationId, IterationState state, string? outputJson, double costUsd, long tokens, long durationMs, CancellationToken ct = default);

    Task DiscardIterationsAfterAsync(string loopRunId, int iterationNo, CancellationToken ct = default);

    Task AddEvaluationAsync(Evaluation evaluation, IReadOnlyList<EvaluationMetric> metrics, CancellationToken ct = default);

    Task<Evaluation?> GetEvaluationAsync(string iterationId, CancellationToken ct = default);

    Task<IReadOnlyList<EvaluationMetric>> ListMetricsAsync(string evaluationId, CancellationToken ct = default);
}
