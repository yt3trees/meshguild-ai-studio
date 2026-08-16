using Cronos;
using WorkAgents.Infrastructure.Execution;

namespace WorkAgents.UnitTests;

public sealed class ScheduleCalculatorTests
{
    [Theory]
    [InlineData("0 9 * * 1", 9, 0, 0)]
    [InlineData("30 0 9 * * *", 9, 0, 30)]
    public void GetNextOccurrenceSupportsStandardAndSecondPrecisionCron(
        string cron,
        int expectedHour,
        int expectedMinute,
        int expectedSecond)
    {
        var now = new DateTimeOffset(2026, 7, 20, 8, 30, 0, TimeSpan.Zero);

        var next = ScheduleCalculator.GetNextOccurrence(cron, now, TimeZoneInfo.Utc);

        Assert.Equal(
            new DateTimeOffset(2026, 7, 20, expectedHour, expectedMinute, expectedSecond, TimeSpan.Zero),
            next);
    }

    [Fact]
    public void GetNextOccurrenceReturnsNullForEmptyCron()
    {
        var next = ScheduleCalculator.GetNextOccurrence(null, DateTimeOffset.UtcNow, TimeZoneInfo.Utc);

        Assert.Null(next);
    }

    [Fact]
    public void GetNextOccurrenceRejectsInvalidCron()
    {
        Assert.Throws<CronFormatException>(() =>
            ScheduleCalculator.GetNextOccurrence("not a cron", DateTimeOffset.UtcNow, TimeZoneInfo.Utc));
    }
}