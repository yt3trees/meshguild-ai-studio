using WorkAgents.Core.Triggers;
using WorkAgents.Infrastructure.Execution;

namespace WorkAgents.UnitTests.Triggers;

public sealed class TriggerScheduleTests
{
    [Fact]
    public void CronAndIntervalReturnTheNextDueTime()
    {
        var now = new DateTimeOffset(2026, 8, 11, 0, 0, 0, TimeSpan.Zero);
        var interval = new TriggerDefinition
        {
            TriggerId = "interval",
            Name = "interval",
            Kind = TriggerKind.Interval,
            TargetKind = "team",
            TargetName = "team",
            Input = "input",
            IntervalSeconds = 30,
        };
        var schedule = interval with { TriggerId = "schedule", Name = "schedule", Kind = TriggerKind.Schedule, Cron = "0 * * * *" };

        Assert.Equal(now.AddSeconds(30), TriggerScheduleCalculator.GetNextOccurrence(interval, now));
        Assert.Equal(now.AddHours(1), TriggerScheduleCalculator.GetNextOccurrence(schedule, now));
    }
}
