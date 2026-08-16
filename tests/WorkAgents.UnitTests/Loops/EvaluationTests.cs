using WorkAgents.Core.Loops;
using WorkAgents.Orchestration.Loops;

namespace WorkAgents.UnitTests.Loops;

public sealed class EvaluationTests
{
    [Fact]
    public void Evaluate_RequiresScoreThresholdAndAllMetrics()
    {
        var evaluator = new Evaluator();
        var result = evaluator.Evaluate(new EvaluationInput
        {
            IterationId = "iteration",
            EvaluatorRef = "tests",
            EvaluatorKind = EvaluatorKind.Deterministic,
            Score = 0.95,
            ScoreThreshold = 0.9,
            Metrics = [Evaluator.Metric("tests_passed_ratio", 0.8, 1.0)],
        });

        Assert.False(result.Evaluation.Passed);
        Assert.Contains(result.Metrics, metric => metric.Name == "tests_passed_ratio" && !metric.Achieved);
    }

    [Fact]
    public void Evaluate_RejectsScoresOutsideRange()
    {
        var evaluator = new Evaluator();

        Assert.Throws<ArgumentOutOfRangeException>(() => evaluator.Evaluate(new EvaluationInput
        {
            IterationId = "iteration",
            EvaluatorRef = "tests",
            EvaluatorKind = EvaluatorKind.Deterministic,
            Score = 1.1,
        }));
    }
}
