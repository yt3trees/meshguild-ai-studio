using WorkAgents.Core;
using WorkAgents.Core.Missions;
using WorkAgents.Infrastructure.Stores;
using WorkAgents.Orchestration.Replay;

namespace WorkAgents.UnitTests.Replay;

public sealed class MissionQueryTests
{
    [Fact]
    public async Task Query_FiltersMissionResultByTeamAndPeriod()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), "work-agents-tests", Guid.NewGuid().ToString("N"), "query.db");
        try
        {
            var store = new SqliteMissionStore(databasePath);
            var created = DateTimeOffset.UtcNow;
            await store.CreateAsync(new Mission
            {
                MissionId = "mission",
                Goal = "goal",
                TargetKind = MissionTargetKind.Team,
                TargetName = "team",
                TeamName = "team",
                Status = MissionStatus.Succeeded,
                Outcome = MissionOutcome.Succeeded,
                CreatedAt = created,
            });
            var replay = new ReplayService(new SqliteMessageStore(databasePath), store);

            var result = await replay.QueryAsync(new WorkAgents.Core.Abstractions.MissionQuery
            {
                Outcomes = [MissionOutcome.Succeeded],
                TeamName = "team",
                From = created.AddMinutes(-1),
                To = created.AddMinutes(1),
            });

            Assert.Single(result);
            Assert.Equal("mission", result[0].MissionId);
        }
        finally
        {
            var directory = Path.GetDirectoryName(databasePath);
            if (directory is not null && Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task ReportBuilderAggregatesAgentAndIterationCosts()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), "work-agents-tests", Guid.NewGuid().ToString("N"), "costs.db");
        try
        {
            var costs = new SqliteCostStore(databasePath);
            await costs.RecordAsync(new CostRecord
            {
                AgentName = "dev",
                MissionId = "mission",
                IterationId = "iteration-1",
                TotalTokens = 10,
                EstimatedCostUsd = 0.2,
            });
            var report = await new MissionReportBuilder(costs).BuildAsync("mission");

            Assert.Equal(10, report.ByAgent.Single().Tokens);
            Assert.Equal(0.2, report.ByIteration.Single().EstimatedCostUsd);
        }
        finally
        {
            var directory = Path.GetDirectoryName(databasePath);
            if (directory is not null && Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }
}
