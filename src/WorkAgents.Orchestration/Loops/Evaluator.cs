using WorkAgents.Core.Loops;

namespace WorkAgents.Orchestration.Loops;

public sealed record EvaluationInput
{
    public required string IterationId { get; init; }

    public required string EvaluatorRef { get; init; }

    public required EvaluatorKind EvaluatorKind { get; init; }

    public double Score { get; init; }

    public string? Notes { get; init; }

    public IReadOnlyList<EvaluationMetric> Metrics { get; init; } = Array.Empty<EvaluationMetric>();

    public double ScoreThreshold { get; init; } = 1.0;
}

public sealed record EvaluationResult(Evaluation Evaluation, IReadOnlyList<EvaluationMetric> Metrics);

/// <summary>Validates and normalizes deterministic evaluation output.</summary>
public sealed class Evaluator
{
    public EvaluationResult Evaluate(EvaluationInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (input.Score is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(input.Score), "Evaluation score must be between 0 and 1.");
        }
        var metrics = input.Metrics.ToArray();
        var passed = input.Score >= input.ScoreThreshold && metrics.All(metric => metric.Achieved);
        var evaluation = new Evaluation
        {
            EvaluationId = Guid.NewGuid().ToString("N"),
            IterationId = input.IterationId,
            Score = input.Score,
            EvaluatorKind = input.EvaluatorKind,
            EvaluatorRef = input.EvaluatorRef,
            Notes = input.Notes,
            Passed = passed,
        };
        return new EvaluationResult(evaluation, metrics);
    }

    public static EvaluationMetric Metric(string name, double value, double target, string? unit = null, string direction = "gte")
    {
        var achieved = string.Equals(direction, "lte", StringComparison.OrdinalIgnoreCase)
            ? value <= target
            : value >= target;
        return new EvaluationMetric
        {
            MetricId = Guid.NewGuid().ToString("N"),
            EvaluationId = string.Empty,
            Name = name,
            Value = value,
            Target = target,
            Achieved = achieved,
            Unit = unit,
        };
    }
}
