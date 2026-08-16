using System.Globalization;
using Microsoft.Data.Sqlite;
using WorkAgents.Core;
using WorkAgents.Core.Abstractions;

namespace WorkAgents.Infrastructure.Stores;

/// <summary>Localプロファイル用のSQLite Runストア。</summary>
public sealed class SqliteRunStore : IRunStore
{
    private readonly string _connectionString;
    private readonly Task _initialization;

    public SqliteRunStore(string databasePath)
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

    public Task CreateAsync(string runId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        return CreateAsync(new RunRecord
        {
            RunId = runId,
            AgentName = string.Empty,
            UserMessage = string.Empty,
        }, ct);
    }

    public async Task CreateAsync(RunRecord run, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(run);
        await EnsureInitializedAsync(ct);

        await using var connection = await OpenConnectionAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO runs (
                run_id, agent_name, user_message, thread_id, status,
                created_at, started_at, completed_at, result, error)
            VALUES ($run_id, $agent_name, $user_message, $thread_id, $status,
                $created_at, $started_at, $completed_at, $result, $error);
            """;
        AddRunParameters(command, run);
        await command.ExecuteNonQueryAsync(ct);
    }

    public async Task<RunRecord?> GetAsync(string runId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        await EnsureInitializedAsync(ct);

        await using var connection = await OpenConnectionAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT run_id, agent_name, user_message, thread_id, status,
                   created_at, started_at, completed_at, result, error
            FROM runs
            WHERE run_id = $run_id;
            """;
        command.Parameters.AddWithValue("$run_id", runId);

        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? ReadRun(reader) : null;
    }

    public async Task<IReadOnlyList<RunRecord>> ListAsync(CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        await using var connection = await OpenConnectionAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT run_id, agent_name, user_message, thread_id, status,
                   created_at, started_at, completed_at, result, error
            FROM runs
            ORDER BY created_at DESC;
            """;

        var runs = new List<RunRecord>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            runs.Add(ReadRun(reader));
        }

        return runs;
    }

    public async Task<RunStatus?> GetStatusAsync(string runId, CancellationToken ct = default)
    {
        var run = await GetAsync(runId, ct);
        return run?.Status;
    }

    public async Task SetStatusAsync(string runId, RunStatus status, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        await EnsureInitializedAsync(ct);

        await using var connection = await OpenConnectionAsync(ct);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(ct);
        var currentStatus = await ReadStatusAsync(connection, transaction, runId, ct)
            ?? throw new KeyNotFoundException($"Run not found: '{runId}'.");

        if (currentStatus == status)
        {
            return;
        }

        EnsureTransition(currentStatus, status);
        await UpdateStatusAsync(connection, transaction, runId, status, ct);
        await transaction.CommitAsync(ct);
    }

    public async Task<bool> TrySetStatusAsync(
        string runId,
        RunStatus expectedStatus,
        RunStatus status,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        await EnsureInitializedAsync(ct);
        EnsureTransition(expectedStatus, status);

        await using var connection = await OpenConnectionAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE runs
            SET status = $status,
                started_at = CASE WHEN $status = $running THEN COALESCE(started_at, $now) ELSE started_at END,
                completed_at = CASE WHEN $status IN ($succeeded, $failed, $aborted) THEN COALESCE(completed_at, $now) ELSE completed_at END
            WHERE run_id = $run_id AND status = $expected_status;
            """;
        AddStatusParameters(command, runId, expectedStatus, status);
        return await command.ExecuteNonQueryAsync(ct) == 1;
    }

    public async Task CompleteAsync(
        string runId,
        RunStatus status,
        string? result = null,
        string? error = null,
        CancellationToken ct = default)
    {
        if (status is not (RunStatus.Succeeded or RunStatus.Failed or RunStatus.Aborted))
        {
            throw new ArgumentOutOfRangeException(nameof(status), status, "Completion status must be terminal.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        await EnsureInitializedAsync(ct);

        await using var connection = await OpenConnectionAsync(ct);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(ct);
        var currentStatus = await ReadStatusAsync(connection, transaction, runId, ct)
            ?? throw new KeyNotFoundException($"Run not found: '{runId}'.");
        EnsureTransition(currentStatus, status);

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE runs
            SET status = $status, completed_at = $completed_at, result = $result, error = $error
            WHERE run_id = $run_id AND status = $current_status;
            """;
        command.Parameters.AddWithValue("$status", (int)status);
        command.Parameters.AddWithValue("$completed_at", FormatDate(DateTimeOffset.UtcNow));
        command.Parameters.AddWithValue("$result", (object?)result ?? DBNull.Value);
        command.Parameters.AddWithValue("$error", (object?)error ?? DBNull.Value);
        command.Parameters.AddWithValue("$run_id", runId);
        command.Parameters.AddWithValue("$current_status", (int)currentStatus);
        if (await command.ExecuteNonQueryAsync(ct) != 1)
        {
            throw new InvalidOperationException($"Run status changed concurrently: '{runId}'.");
        }

        await transaction.CommitAsync(ct);
    }

    private async Task InitializeAsync()
    {
        await using var connection = await OpenConnectionAsync(CancellationToken.None);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA journal_mode = WAL;
            CREATE TABLE IF NOT EXISTS runs (
                run_id TEXT NOT NULL PRIMARY KEY,
                agent_name TEXT NOT NULL,
                user_message TEXT NOT NULL,
                thread_id TEXT NULL,
                status INTEGER NOT NULL,
                created_at TEXT NOT NULL,
                started_at TEXT NULL,
                completed_at TEXT NULL,
                result TEXT NULL,
                error TEXT NULL
            );
            CREATE INDEX IF NOT EXISTS ix_runs_status_created_at
                ON runs(status, created_at DESC);
            """;
        await command.ExecuteNonQueryAsync();
    }

    private async Task EnsureInitializedAsync(CancellationToken ct)
    {
        await _initialization.WaitAsync(ct);
    }

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

    private static async Task<RunStatus?> ReadStatusAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string runId,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT status FROM runs WHERE run_id = $run_id;";
        command.Parameters.AddWithValue("$run_id", runId);
        var value = await command.ExecuteScalarAsync(ct);
        return value is null or DBNull ? null : (RunStatus)Convert.ToInt32(value, CultureInfo.InvariantCulture);
    }

    private static async Task UpdateStatusAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string runId,
        RunStatus status,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE runs
            SET status = $status,
                started_at = CASE WHEN $status = $running THEN COALESCE(started_at, $now) ELSE started_at END,
                completed_at = CASE WHEN $status IN ($succeeded, $failed, $aborted) THEN COALESCE(completed_at, $now) ELSE completed_at END
            WHERE run_id = $run_id;
            """;
        command.Parameters.AddWithValue("$status", (int)status);
        command.Parameters.AddWithValue("$running", (int)RunStatus.Running);
        command.Parameters.AddWithValue("$succeeded", (int)RunStatus.Succeeded);
        command.Parameters.AddWithValue("$failed", (int)RunStatus.Failed);
        command.Parameters.AddWithValue("$aborted", (int)RunStatus.Aborted);
        command.Parameters.AddWithValue("$now", FormatDate(DateTimeOffset.UtcNow));
        command.Parameters.AddWithValue("$run_id", runId);
        if (await command.ExecuteNonQueryAsync(ct) != 1)
        {
            throw new InvalidOperationException($"Run status update failed: '{runId}'.");
        }
    }

    private static void AddStatusParameters(
        SqliteCommand command,
        string runId,
        RunStatus expectedStatus,
        RunStatus status)
    {
        command.Parameters.AddWithValue("$status", (int)status);
        command.Parameters.AddWithValue("$expected_status", (int)expectedStatus);
        command.Parameters.AddWithValue("$running", (int)RunStatus.Running);
        command.Parameters.AddWithValue("$succeeded", (int)RunStatus.Succeeded);
        command.Parameters.AddWithValue("$failed", (int)RunStatus.Failed);
        command.Parameters.AddWithValue("$aborted", (int)RunStatus.Aborted);
        command.Parameters.AddWithValue("$now", FormatDate(DateTimeOffset.UtcNow));
        command.Parameters.AddWithValue("$run_id", runId);
    }

    private static void AddRunParameters(SqliteCommand command, RunRecord run)
    {
        command.Parameters.AddWithValue("$run_id", run.RunId);
        command.Parameters.AddWithValue("$agent_name", run.AgentName);
        command.Parameters.AddWithValue("$user_message", run.UserMessage);
        command.Parameters.AddWithValue("$thread_id", (object?)run.ThreadId ?? DBNull.Value);
        command.Parameters.AddWithValue("$status", (int)run.Status);
        command.Parameters.AddWithValue("$created_at", FormatDate(run.CreatedAt));
        command.Parameters.AddWithValue("$started_at", (object?)FormatDate(run.StartedAt) ?? DBNull.Value);
        command.Parameters.AddWithValue("$completed_at", (object?)FormatDate(run.CompletedAt) ?? DBNull.Value);
        command.Parameters.AddWithValue("$result", (object?)run.Result ?? DBNull.Value);
        command.Parameters.AddWithValue("$error", (object?)run.Error ?? DBNull.Value);
    }

    private static RunRecord ReadRun(SqliteDataReader reader)
    {
        return new RunRecord
        {
            RunId = reader.GetString(0),
            AgentName = reader.GetString(1),
            UserMessage = reader.GetString(2),
            ThreadId = ReadNullableString(reader, 3),
            Status = (RunStatus)reader.GetInt32(4),
            CreatedAt = ParseDate(reader.GetString(5)),
            StartedAt = ParseNullableDate(reader, 6),
            CompletedAt = ParseNullableDate(reader, 7),
            Result = ReadNullableString(reader, 8),
            Error = ReadNullableString(reader, 9),
        };
    }

    private static string? ReadNullableString(SqliteDataReader reader, int ordinal)
    {
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }

    private static DateTimeOffset? ParseNullableDate(SqliteDataReader reader, int ordinal)
    {
        return reader.IsDBNull(ordinal) ? null : ParseDate(reader.GetString(ordinal));
    }

    private static string? FormatDate(DateTimeOffset? value)
    {
        return value?.ToString("O", CultureInfo.InvariantCulture);
    }

    private static DateTimeOffset ParseDate(string value)
    {
        return DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
    }

    private static void EnsureTransition(RunStatus from, RunStatus to)
    {
        if (!RunStatusMachine.CanTransition(from, to))
        {
            throw new InvalidOperationException($"Invalid run status transition: {from} -> {to}.");
        }
    }
}