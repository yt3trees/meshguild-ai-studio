using WorkAgents.Orchestration.Graph;

namespace WorkAgents.UnitTests.Graphs;

public sealed class ExpressionEvaluatorTests
{
    [Fact]
    public void EvaluateBoolean_SupportsReferencesComparisonsAndLogic()
    {
        var evaluator = new ExpressionEvaluator();
        var values = new Dictionary<string, object?>
        {
            ["nodes.verify.output.score"] = 0.95,
            ["nodes.verify.output.failed"] = false,
        };

        Assert.True(evaluator.EvaluateBoolean("${nodes.verify.output.score} >= 0.9 && !${nodes.verify.output.failed}", values));
    }

    [Fact]
    public void EvaluateBoolean_RejectsUnknownReferencesAndUnsupportedCharacters()
    {
        var evaluator = new ExpressionEvaluator();
        var values = new Dictionary<string, object?>();

        Assert.Throws<KeyNotFoundException>(() => evaluator.EvaluateBoolean("${nodes.unknown.output} == 1", values));
        Assert.Throws<KeyNotFoundException>(() => evaluator.EvaluateBoolean("System.IO.File.Delete('x')", values));
    }
}
