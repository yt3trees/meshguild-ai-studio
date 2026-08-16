using Cronos;

namespace WorkAgents.Infrastructure.Execution;

public static class ScheduleCalculator
{
    public static DateTimeOffset? GetNextOccurrence(
        string? cron,
        DateTimeOffset now,
        TimeZoneInfo timeZone)
    {
        if (string.IsNullOrWhiteSpace(cron))
        {
            return null;
        }

        var fieldCount = cron.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
        var format = fieldCount == 6 ? CronFormat.IncludeSeconds : CronFormat.Standard;
        var expression = CronExpression.Parse(cron, format);
        var nextUtc = expression.GetNextOccurrence(now.UtcDateTime, timeZone);
        return nextUtc is null
            ? null
            : new DateTimeOffset(nextUtc.Value, TimeSpan.Zero);
    }
}