using WorkAgents.Core;
using WorkAgents.Infrastructure.Stores;

namespace WorkAgents.UnitTests;

public sealed class SqliteScheduleStoreTests
{
    [Fact]
    public async Task UpsertAndListRoundtrips()
    {
        var path = CreateDatabasePath();
        var store = new SqliteScheduleStore(path);
        try
        {
            var def = new ScheduleDefinition
            {
                Name = "weekly-meeting",
                WorkflowName = "weekly-meeting",
                Input = "hello",
                Cron = "0 9 * * 1",
                Enabled = true,
                NextRunAt = new DateTimeOffset(2026, 7, 20, 9, 0, 0, TimeSpan.FromHours(9)),
            };
            await store.UpsertAsync(def);

            var list = await store.ListAsync();
            Assert.Single(list);
            Assert.Equal("weekly-meeting", list[0].Name);
            Assert.True(list[0].Enabled);
            Assert.Equal("0 9 * * 1", list[0].Cron);

            var updated = def with { Enabled = false, UpdatedAt = DateTimeOffset.UtcNow };
            await store.UpsertAsync(updated);
            var got = await store.GetAsync("weekly-meeting");
            Assert.NotNull(got);
            Assert.False(got!.Enabled);
        }
        finally
        {
            DeleteDatabaseDirectory(path);
        }
    }

    [Fact]
    public async Task ListDueReturnsOnlyEnabledAndPast()
    {
        var path = CreateDatabasePath();
        var store = new SqliteScheduleStore(path);
        try
        {
            var now = DateTimeOffset.UtcNow;
            await store.UpsertAsync(new ScheduleDefinition
            {
                Name = "due-enabled",
                WorkflowName = "w",
                Cron = "0 9 * * 1",
                NextRunAt = now.AddMinutes(-5),
                Enabled = true,
            });
            await store.UpsertAsync(new ScheduleDefinition
            {
                Name = "future-enabled",
                WorkflowName = "w",
                Cron = "0 9 * * 1",
                NextRunAt = now.AddMinutes(30),
                Enabled = true,
            });
            await store.UpsertAsync(new ScheduleDefinition
            {
                Name = "past-disabled",
                WorkflowName = "w",
                Cron = "0 9 * * 1",
                NextRunAt = now.AddMinutes(-60),
                Enabled = false,
            });

            var due = await store.ListDueAsync(now);
            Assert.Single(due);
            Assert.Equal("due-enabled", due[0].Name);
        }
        finally
        {
            DeleteDatabaseDirectory(path);
        }
    }

    [Fact]
    public async Task UpdateAfterFireWritesLastAndNext()
    {
        var path = CreateDatabasePath();
        var store = new SqliteScheduleStore(path);
        try
        {
            await store.UpsertAsync(new ScheduleDefinition
            {
                Name = "s",
                WorkflowName = "w",
                Cron = "0 9 * * 1",
                Enabled = true,
                NextRunAt = DateTimeOffset.UtcNow.AddMinutes(-1),
            });

            var last = DateTimeOffset.UtcNow;
            var next = last.AddDays(7);
            await store.UpdateAfterFireAsync("s", last, next);

            var got = await store.GetAsync("s");
            Assert.NotNull(got);
            Assert.Equal(next.UtcDateTime, got!.NextRunAt!.Value.UtcDateTime, TimeSpan.FromSeconds(1));
            Assert.Equal(last.UtcDateTime, got.LastRunAt!.Value.UtcDateTime, TimeSpan.FromSeconds(1));
        }
        finally
        {
            DeleteDatabaseDirectory(path);
        }
    }

    [Fact]
    public async Task DeleteRemovesRow()
    {
        var path = CreateDatabasePath();
        var store = new SqliteScheduleStore(path);
        try
        {
            await store.UpsertAsync(new ScheduleDefinition
            {
                Name = "removable",
                WorkflowName = "w",
                Cron = null,
                Enabled = true,
            });
            await store.DeleteAsync("removable");
            var got = await store.GetAsync("removable");
            Assert.Null(got);
        }
        finally
        {
            DeleteDatabaseDirectory(path);
        }
    }

    private static string CreateDatabasePath()
        => Path.Combine(Path.GetTempPath(), "work-agents-tests", $"{Guid.NewGuid():N}", "runs.db");

    private static void DeleteDatabaseDirectory(string databasePath)
    {
        var directory = Path.GetDirectoryName(databasePath);
        if (directory is not null && Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}