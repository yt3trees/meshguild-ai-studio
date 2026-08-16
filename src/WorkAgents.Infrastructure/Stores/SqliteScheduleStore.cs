using System.Globalization;
using Microsoft.Data.Sqlite;
using WorkAgents.Core;
using WorkAgents.Core.Abstractions;

namespace WorkAgents.Infrastructure.Stores;

/// <summary>Localプロファイル用のSQLiteスケジュールストア(5.13.2)。</summary>
public sealed class SqliteScheduleStore : IScheduleStore
{
    private readonly string _connectionString;
    private readonly Task _initialization;

    public SqliteScheduleStore(string databasePath)
    {
        if (string.IsNullOrWhiteSpace(databasePath))
        {
            throw new ArgumentException("Database path is required.", nameof(databasePath));
        }

        var fullPath = Path.GetFullPath(databasePath);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = fullPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            Pooling = false,
        }.ToString();
        _initialization = InitializeAsync();
    }

    public async Task<IReadOnlyList<ScheduleDefinition>> ListAsync(CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        await using var connection = await OpenConnectionAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT name, workflow_name, input, cron, enabled,
                   last_run_at, next_run_at, created_at, updated_at
            FROM schedules
            ORDER BY name ASC;
            """;

        var list = new List<ScheduleDefinition>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            list.Add(ReadSchedule(reader));
        }
        return list;
    }

    public async Task<ScheduleDefinition?> GetAsync(string name, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        await EnsureInitializedAsync(ct);
        await using var connection = await OpenConnectionAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT name, workflow_name, input, cron, enabled,
                   last_run_at, next_run_at, created_at, updated_at
            FROM schedules
            WHERE name = $name;
            """;
        command.Parameters.AddWithValue("$name", name);
        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? ReadSchedule(reader) : null;
    }

    public async Task UpsertAsync(ScheduleDefinition definition, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentException.ThrowIfNullOrWhiteSpace(definition.Name);
        ArgumentException.ThrowIfNullOrWhiteSpace(definition.WorkflowName);
        await EnsureInitializedAsync(ct);

        await using var connection = await OpenConnectionAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO schedules (
                name, workflow_name, input, cron, enabled,
                last_run_at, next_run_at, created_at, updated_at)
            VALUES ($name, $workflow_name, $input, $cron, $enabled,
                $last_run_at, $next_run_at, $created_at, $updated_at)
            ON CONFLICT(name) DO UPDATE SET
                workflow_name = excluded.workflow_name,
                input = excluded.input,
                cron = excluded.cron,
                enabled = excluded.enabled,
                last_run_at = excluded.last_run_at,
                next_run_at = excluded.next_run_at,
                updated_at = excluded.updated_at;
            """;
        AddParameters(command, definition);
        await command.ExecuteNonQueryAsync(ct);
    }

    public async Task DeleteAsync(string name, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        await EnsureInitializedAsync(ct);
        await using var connection = await OpenConnectionAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM schedules WHERE name = $name;";
        command.Parameters.AddWithValue("$name", name);
        await command.ExecuteNonQueryAsync(ct);
    }

    public async Task<IReadOnlyList<ScheduleDefinition>> ListDueAsync(DateTimeOffset now, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        await using var connection = await OpenConnectionAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT name, workflow_name, input, cron, enabled,
                   last_run_at, next_run_at, created_at, updated_at
            FROM schedules
            WHERE enabled = 1 AND next_run_at IS NOT NULL AND next_run_at <= $now
            ORDER BY next_run_at ASC;
            """;
        command.Parameters.AddWithValue("$now", FormatDate(now));
        var list = new List<ScheduleDefinition>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            list.Add(ReadSchedule(reader));
        }
        return list;
    }

    public async Task UpdateAfterFireAsync(
        string name,
        DateTimeOffset lastRunAt,
        DateTimeOffset nextRunAt,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        await EnsureInitializedAsync(ct);
        await using var connection = await OpenConnectionAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE schedules
            SET last_run_at = $last_run_at,
                next_run_at = $next_run_at,
                updated_at = $updated_at
            WHERE name = $name;
            """;
        command.Parameters.AddWithValue("$name", name);
        command.Parameters.AddWithValue("$last_run_at", FormatDate(lastRunAt));
        command.Parameters.AddWithValue("$next_run_at", FormatDate(nextRunAt));
        command.Parameters.AddWithValue("$updated_at", FormatDate(DateTimeOffset.UtcNow));
        await command.ExecuteNonQueryAsync(ct);
    }

    private async Task InitializeAsync()
    {
        await using var connection = await OpenConnectionAsync(CancellationToken.None);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA journal_mode = WAL;
            CREATE TABLE IF NOT EXISTS schedules (
                name TEXT NOT NULL PRIMARY KEY,
                workflow_name TEXT NOT NULL,
                input TEXT NOT NULL,
                cron TEXT NULL,
                enabled INTEGER NOT NULL,
                last_run_at TEXT NULL,
                next_run_at TEXT NULL,
                created_at TEXT NOT NULL,
                updated_at TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS ix_schedules_due
                ON schedules(enabled, next_run_at);
            """;
        await command.ExecuteNonQueryAsync();
    }

    private async Task EnsureInitializedAsync(CancellationToken ct) => await _initialization.WaitAsync(ct);

    private async Task<SqliteConnection> OpenConnectionAsync(CancellationToken ct)
    {
        var connection = new SqliteConnection(_connectionString);
        try
        {
            await connection.OpenAsync(ct);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    private static void AddParameters(SqliteCommand command, ScheduleDefinition s)
    {
        command.Parameters.AddWithValue("$name", s.Name);
        command.Parameters.AddWithValue("$workflow_name", s.WorkflowName);
        command.Parameters.AddWithValue("$input", s.Input ?? string.Empty);
        command.Parameters.AddWithValue("$cron", (object?)s.Cron ?? DBNull.Value);
        command.Parameters.AddWithValue("$enabled", s.Enabled ? 1 : 0);
        command.Parameters.AddWithValue("$last_run_at", (object?)FormatDate(s.LastRunAt) ?? DBNull.Value);
        command.Parameters.AddWithValue("$next_run_at", (object?)FormatDate(s.NextRunAt) ?? DBNull.Value);
        command.Parameters.AddWithValue("$created_at", FormatDate(s.CreatedAt) ?? string.Empty);
        command.Parameters.AddWithValue("$updated_at", FormatDate(s.UpdatedAt) ?? string.Empty);
    }

    private static ScheduleDefinition ReadSchedule(SqliteDataReader reader)
    {
        return new ScheduleDefinition
        {
            Name = reader.GetString(0),
            WorkflowName = reader.GetString(1),
            Input = reader.GetString(2),
            Cron = reader.IsDBNull(3) ? null : reader.GetString(3),
            Enabled = reader.GetInt32(4) != 0,
            LastRunAt = ParseNullableDate(reader, 5),
            NextRunAt = ParseNullableDate(reader, 6),
            CreatedAt = ParseDate(reader.GetString(7)),
            UpdatedAt = ParseDate(reader.GetString(8)),
        };
    }

    private static DateTimeOffset? ParseNullableDate(SqliteDataReader reader, int ordinal)
        => reader.IsDBNull(ordinal) ? null : ParseDate(reader.GetString(ordinal));

    private static string? FormatDate(DateTimeOffset? value)
        => value?.ToString("O", CultureInfo.InvariantCulture);

    private static DateTimeOffset ParseDate(string value)
        => DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
}