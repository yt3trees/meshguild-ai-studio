using WorkAgents.Core.Triggers;

namespace WorkAgents.Infrastructure.Execution;

public static class TriggerScheduleCalculator
{
    public static DateTimeOffset? GetNextOccurrence(TriggerDefinition trigger, DateTimeOffset now, TimeZoneInfo? timeZone = null)
    {
        ArgumentNullException.ThrowIfNull(trigger);
        return trigger.Kind switch
        {
            TriggerKind.Interval when trigger.IntervalSeconds is > 0 => now.AddSeconds(trigger.IntervalSeconds.Value),
            TriggerKind.Schedule => ScheduleCalculator.GetNextOccurrence(trigger.Cron, now, timeZone ?? TimeZoneInfo.Local),
            _ => null,
        };
    }
}
