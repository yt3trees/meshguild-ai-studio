using WorkAgents.Core.Loops;
using WorkAgents.Infrastructure.Stores;
using WorkAgents.Orchestration.Loops;
using WorkAgents.UnitTests.Fakes;
using WorkAgents.UnitTests.Support;

namespace WorkAgents.UnitTests.Loops;

public sealed class LoopExecutorTests
{
    [Fact]
    public async Task Execute_StopsOnSecondIterationWhenEvaluationPasses()
    {
        using var paths = new MissionWorkspaceTestPaths();
        var databasePath = Path.Combine(Path.GetTempPath(), "work-agents-tests", Guid.NewGuid().ToString("N"), "loops.db");
        try
        {
            var invoker = new ScriptedAgentInvoker().Script("tester", "first").Script("tester", "second");
            var executor = new LoopExecutor(invoker, new SqliteLoopStore(databasePath));
            var result = await executor.ExecuteAsync(new LoopExecutionRequest
            {
                MissionId = "mission",
                NodeRunId = "node",
                AgentName = "tester",
                InitialInput = "run tests",
                WorkingDirectory = paths.MissionWorkspace("mission"),
                MaxIterations = 5,
                ScoreThreshold = 0.9,
                EvaluateIteration = (iteration, output, _) => Task.FromResult(
                    iteration == 1
                        ? new LoopIterationEvaluation(0.6, output, "tests still fail")
                        : new LoopIterationEvaluation(1.0, output, "all tests pass")),
            });

            Assert.Equal(2, result.Iterations.Count);
            Assert.Equal(LoopStopReason.StopConditionMet, result.LoopRun.StopReason);
            Assert.Equal("second", result.BestOutput);
            Assert.Equal(2, invoker.Invocations.Count);
            Assert.All(invoker.Invocations, invocation => Assert.Equal(paths.MissionWorkspace("mission"), invocation.WorkingDirectory));
        }
        finally
        {
            var directory = Path.GetDirectoryName(databasePath);
            if (directory is not null && Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Execute_MaxIterationsOneReturnsNotPassedIteration()
    {
        var invoker = new ScriptedAgentInvoker().Script("tester", "not done");
        var result = await new LoopExecutor(invoker).ExecuteAsync(new LoopExecutionRequest
        {
            MissionId = "mission",
            NodeRunId = "node",
            AgentName = "tester",
            InitialInput = "run",
            MaxIterations = 1,
            ScoreThreshold = 0.9,
            EvaluateIteration = (_, output, _) => Task.FromResult(new LoopIterationEvaluation(0.2, output)),
        });

        Assert.Equal(LoopStopReason.MaxIterations, result.LoopRun.StopReason);
        Assert.Single(result.Iterations);
    }
}
