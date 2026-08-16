using System.Globalization;
using System.Text;
using Microsoft.Data.Sqlite;
using WorkAgents.Core.Abstractions;
using WorkAgents.Core.Missions;

namespace WorkAgents.Infrastructure.Stores;

/// <summary>Localプロファイル用のSQLiteミッションストア (missions / budgets テーブル、T027)。</summary>
public sealed class SqliteMissionStore : IMissionStore
{
    private readonly string _connectionString;
    private readonly ISecretRedactor? _redactor;
    private readonly Task _initialization;

    public SqliteMissionStore(string databasePath, ISecretRedactor? redactor = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);

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
        _redactor = redactor;
        _initialization = InitializeAsync();
    }

    public async Task CreateAsync(Mission mission, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(mission);
        await EnsureInitializedAsync(ct);

        await using var connection = await OpenConnectionAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO missions (
                mission_id, goal, target_kind, target_name, graph_version_id, team_name,
                status, trigger_id, trigger_kind, queued_reason, queue_position,
                outcome, stop_reason, error, created_at, started_at, completed_at)
            VALUES (
                $mission_id, $goal, $target_kind, $target_name, $graph_version_id, $team_name,
                $status, $trigger_id, $trigger_kind, $queued_reason, $queue_position,
                $outcome, $stop_reason, $error, $created_at, $started_at, $completed_at);
            """;
        AddMissionParameters(command, mission);
        await command.ExecuteNonQueryAsync(ct);
    }

    public async Task<Mission?> GetAsync(string missionId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(missionId);
        await EnsureInitializedAsync(ct);

        await using var connection = await OpenConnectionAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = SelectSql + " WHERE mission_id = $mission_id;";
        command.Parameters.AddWithValue("$mission_id", missionId);

        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? ReadMission(reader) : null;
    }

    public async Task<IReadOnlyList<Mission>> ListAsync(MissionQuery? query = null, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        await using var connection = await OpenConnectionAsync(ct);
        await using var command = connection.CreateCommand();
        var sql = new StringBuilder(SelectSql);
        var conditions = new List<string>();

        query ??= new MissionQuery();

        if (query.Outcomes is { Count: > 0 })
        {
            var names = query.Outcomes.Select((o, i) => $"$outcome{i}").ToList();
            for (var i = 0; i < query.Outcomes.Count; i++)
            {
                command.Parameters.AddWithValue($"$outcome{i}", query.Outcomes[i].ToString());
            }
            conditions.Add($"outcome IN ({string.Join(",", names)})");
        }

        if (query.Statuses is { Count: > 0 })
        {
            var names = query.Statuses.Select((s, i) => $"$status{i}").ToList();
            for (var i = 0; i < query.Statuses.Count; i++)
            {
                command.Parameters.AddWithValue($"$status{i}", query.Statuses[i].ToString());
            }
            conditions.Add($"status IN ({string.Join(",", names)})");
        }

        if (!string.IsNullOrWhiteSpace(query.TeamName))
        {
            conditions.Add("team_name = $team_name");
            command.Parameters.AddWithValue("$team_name", query.TeamName);
        }

        if (query.From is not null)
        {
            conditions.Add("created_at >= $from");
            command.Parameters.AddWithValue("$from", FormatDate(query.From));
        }

        if (query.To is not null)
        {
            conditions.Add("created_at <= $to");
            command.Parameters.AddWithValue("$to", FormatDate(query.To));
        }

        if (conditions.Count > 0)
        {
            sql.Append(" WHERE ").Append(string.Join(" AND ", conditions));
        }

        sql.Append(" ORDER BY created_at DESC LIMIT $limit OFFSET $offset;");
        command.Parameters.AddWithValue("$limit", query.Limit);
        command.Parameters.AddWithValue("$offset", query.Offset);
        command.CommandText = sql.ToString();

        var missions = new List<Mission>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            missions.Add(ReadMission(reader));
        }
        return missions;
    }

    public async Task SetStatusAsync(
        string missionId,
        MissionStatus status,
        MissionOutcome? outcome = null,
        MissionStopReason? stopReason = null,
        string? error = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(missionId);
        await EnsureInitializedAsync(ct);

        await using var connection = await OpenConnectionAsync(ct);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(ct);
        var current = await ReadStatusAsync(connection, transaction, missionId, ct)
            ?? throw new KeyNotFoundException($"Mission not found: '{missionId}'.");

        MissionStatusMachine.EnsureTransition(current, status);

        var isTerminal = status is MissionStatus.Succeeded or MissionStatus.NotConverged
            or MissionStatus.Failed or MissionStatus.Aborted;

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE missions
            SET status = $status,
                outcome = $outcome,
                stop_reason = $stop_reason,
                error = $error,
                started_at = CASE WHEN $status = 'Running' THEN COALESCE(started_at, $now) ELSE started_at END,
                completed_at = CASE WHEN $is_terminal = 1 THEN COALESCE(completed_at, $now) ELSE completed_at END
            WHERE mission_id = $mission_id;
            """;
        command.Parameters.AddWithValue("$status", status.ToString());
        command.Parameters.AddWithValue("$outcome", (object?)outcome?.ToString() ?? DBNull.Value);
        command.Parameters.AddWithValue("$stop_reason", (object?)stopReason?.ToString() ?? DBNull.Value);
        command.Parameters.AddWithValue("$error", (object?)(_redactor is null || error is null ? error : await _redactor.RedactAsync(error, ct)) ?? DBNull.Value);
        command.Parameters.AddWithValue("$is_terminal", isTerminal ? 1 : 0);
        command.Parameters.AddWithValue("$now", FormatDate(DateTimeOffset.UtcNow));
        command.Parameters.AddWithValue("$mission_id", missionId);
        await command.ExecuteNonQueryAsync(ct);
        await transaction.CommitAsync(ct);
    }

    public async Task SetQueuePositionAsync(
        string missionId,
        MissionQueuedReason? reason,
        int? position,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(missionId);
        await EnsureInitializedAsync(ct);

        await using var connection = await OpenConnectionAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE missions SET queued_reason = $reason, queue_position = $position
            WHERE mission_id = $mission_id;
            """;
        command.Parameters.AddWithValue("$reason", (object?)reason?.ToString() ?? DBNull.Value);
        command.Parameters.AddWithValue("$position", (object?)position ?? DBNull.Value);
        command.Parameters.AddWithValue("$mission_id", missionId);
        await command.ExecuteNonQueryAsync(ct);
    }

    public async Task UpsertBudgetAsync(Budget budget, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(budget);
        await EnsureInitializedAsync(ct);

        await using var connection = await OpenConnectionAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO budgets (
                mission_id, cost_limit_usd, time_limit_seconds, max_iterations, max_concurrent_agents,
                cost_used_usd, elapsed_seconds, iterations_used, peak_concurrent_agents)
            VALUES (
                $mission_id, $cost_limit_usd, $time_limit_seconds, $max_iterations, $max_concurrent_agents,
                $cost_used_usd, $elapsed_seconds, $iterations_used, $peak_concurrent_agents)
            ON CONFLICT(mission_id) DO UPDATE SET
                cost_limit_usd = excluded.cost_limit_usd,
                time_limit_seconds = excluded.time_limit_seconds,
                max_iterations = excluded.max_iterations,
                max_concurrent_agents = excluded.max_concurrent_agents,
                cost_used_usd = excluded.cost_used_usd,
                elapsed_seconds = excluded.elapsed_seconds,
                iterations_used = excluded.iterations_used,
                peak_concurrent_agents = excluded.peak_concurrent_agents;
            """;
        command.Parameters.AddWithValue("$mission_id", budget.MissionId);
        command.Parameters.AddWithValue("$cost_limit_usd", (object?)budget.CostLimitUsd ?? DBNull.Value);
        command.Parameters.AddWithValue("$time_limit_seconds", (object?)budget.TimeLimitSeconds ?? DBNull.Value);
        command.Parameters.AddWithValue("$max_iterations", (object?)budget.MaxIterations ?? DBNull.Value);
        command.Parameters.AddWithValue("$max_concurrent_agents", (object?)budget.MaxConcurrentAgents ?? DBNull.Value);
        command.Parameters.AddWithValue("$cost_used_usd", budget.CostUsedUsd);
        command.Parameters.AddWithValue("$elapsed_seconds", budget.ElapsedSeconds);
        command.Parameters.AddWithValue("$iterations_used", budget.IterationsUsed);
        command.Parameters.AddWithValue("$peak_concurrent_agents", budget.PeakConcurrentAgents);
        await command.ExecuteNonQueryAsync(ct);
    }

    public async Task<Budget?> GetBudgetAsync(string missionId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(missionId);
        await EnsureInitializedAsync(ct);

        await using var connection = await OpenConnectionAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT mission_id, cost_limit_usd, time_limit_seconds, max_iterations, max_concurrent_agents,
                   cost_used_usd, elapsed_seconds, iterations_used, peak_concurrent_agents
            FROM budgets WHERE mission_id = $mission_id;
            """;
        command.Parameters.AddWithValue("$mission_id", missionId);

        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
        {
            return null;
        }

        return new Budget
        {
            MissionId = reader.GetString(0),
            CostLimitUsd = reader.IsDBNull(1) ? null : reader.GetDouble(1),
            TimeLimitSeconds = reader.IsDBNull(2) ? null : reader.GetInt32(2),
            MaxIterations = reader.IsDBNull(3) ? null : reader.GetInt32(3),
            MaxConcurrentAgents = reader.IsDBNull(4) ? null : reader.GetInt32(4),
            CostUsedUsd = reader.GetDouble(5),
            ElapsedSeconds = reader.GetInt32(6),
            IterationsUsed = reader.GetInt32(7),
            PeakConcurrentAgents = reader.GetInt32(8),
        };
    }

    private const string SelectSql = """
        SELECT mission_id, goal, target_kind, target_name, graph_version_id, team_name,
               status, trigger_id, trigger_kind, queued_reason, queue_position,
               outcome, stop_reason, error, created_at, started_at, completed_at
        FROM missions
        """;

    private async Task InitializeAsync()
    {
        await using var connection = await OpenConnectionAsync(CancellationToken.None);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA journal_mode = WAL;
            CREATE TABLE IF NOT EXISTS missions (
                mission_id TEXT NOT NULL PRIMARY KEY,
                goal TEXT NOT NULL,
                target_kind TEXT NOT NULL,
                target_name TEXT NOT NULL,
                graph_version_id TEXT NULL,
                team_name TEXT NULL,
                status TEXT NOT NULL,
                trigger_id TEXT NULL,
                trigger_kind TEXT NOT NULL,
                queued_reason TEXT NULL,
                queue_position INTEGER NULL,
                outcome TEXT NULL,
                stop_reason TEXT NULL,
                error TEXT NULL,
                created_at TEXT NOT NULL,
                started_at TEXT NULL,
                completed_at TEXT NULL
            );
            CREATE INDEX IF NOT EXISTS ix_missions_status_created_at ON missions(status, created_at DESC);
            CREATE INDEX IF NOT EXISTS ix_missions_team_name ON missions(team_name);

            CREATE TABLE IF NOT EXISTS budgets (
                mission_id TEXT NOT NULL PRIMARY KEY,
                cost_limit_usd REAL NULL,
                time_limit_seconds INTEGER NULL,
                max_iterations INTEGER NULL,
                max_concurrent_agents INTEGER NULL,
                cost_used_usd REAL NOT NULL DEFAULT 0,
                elapsed_seconds INTEGER NOT NULL DEFAULT 0,
                iterations_used INTEGER NOT NULL DEFAULT 0,
                peak_concurrent_agents INTEGER NOT NULL DEFAULT 0
            );
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

    private static async Task<MissionStatus?> ReadStatusAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string missionId,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT status FROM missions WHERE mission_id = $mission_id;";
        command.Parameters.AddWithValue("$mission_id", missionId);
        var value = await command.ExecuteScalarAsync(ct);
        return value is null or DBNull ? null : Enum.Parse<MissionStatus>((string)value);
    }

    private static void AddMissionParameters(SqliteCommand command, Mission mission)
    {
        command.Parameters.AddWithValue("$mission_id", mission.MissionId);
        command.Parameters.AddWithValue("$goal", mission.Goal);
        command.Parameters.AddWithValue("$target_kind", mission.TargetKind.ToString());
        command.Parameters.AddWithValue("$target_name", mission.TargetName);
        command.Parameters.AddWithValue("$graph_version_id", (object?)mission.GraphVersionId ?? DBNull.Value);
        command.Parameters.AddWithValue("$team_name", (object?)mission.TeamName ?? DBNull.Value);
        command.Parameters.AddWithValue("$status", mission.Status.ToString());
        command.Parameters.AddWithValue("$trigger_id", (object?)mission.TriggerId ?? DBNull.Value);
        command.Parameters.AddWithValue("$trigger_kind", mission.TriggerKind.ToString());
        command.Parameters.AddWithValue("$queued_reason", (object?)mission.QueuedReason?.ToString() ?? DBNull.Value);
        command.Parameters.AddWithValue("$queue_position", (object?)mission.QueuePosition ?? DBNull.Value);
        command.Parameters.AddWithValue("$outcome", (object?)mission.Outcome?.ToString() ?? DBNull.Value);
        command.Parameters.AddWithValue("$stop_reason", (object?)mission.StopReason?.ToString() ?? DBNull.Value);
        command.Parameters.AddWithValue("$error", (object?)mission.Error ?? DBNull.Value);
        command.Parameters.AddWithValue("$created_at", FormatDate(mission.CreatedAt));
        command.Parameters.AddWithValue("$started_at", (object?)FormatDate(mission.StartedAt) ?? DBNull.Value);
        command.Parameters.AddWithValue("$completed_at", (object?)FormatDate(mission.CompletedAt) ?? DBNull.Value);
    }

    private static Mission ReadMission(SqliteDataReader reader)
    {
        return new Mission
        {
            MissionId = reader.GetString(0),
            Goal = reader.GetString(1),
            TargetKind = Enum.Parse<MissionTargetKind>(reader.GetString(2)),
            TargetName = reader.GetString(3),
            GraphVersionId = ReadNullableString(reader, 4),
            TeamName = ReadNullableString(reader, 5),
            Status = Enum.Parse<MissionStatus>(reader.GetString(6)),
            TriggerId = ReadNullableString(reader, 7),
            TriggerKind = Enum.Parse<MissionTriggerKind>(reader.GetString(8)),
            QueuedReason = ReadNullableString(reader, 9) is { } qr ? Enum.Parse<MissionQueuedReason>(qr) : null,
            QueuePosition = reader.IsDBNull(10) ? null : reader.GetInt32(10),
            Outcome = ReadNullableString(reader, 11) is { } oc ? Enum.Parse<MissionOutcome>(oc) : null,
            StopReason = ReadNullableString(reader, 12) is { } sr ? Enum.Parse<MissionStopReason>(sr) : null,
            Error = ReadNullableString(reader, 13),
            CreatedAt = ParseDate(reader.GetString(14)),
            StartedAt = ParseNullableDate(reader, 15),
            CompletedAt = ParseNullableDate(reader, 16),
        };
    }

    private static string? ReadNullableString(SqliteDataReader reader, int ordinal)
        => reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);

    private static DateTimeOffset? ParseNullableDate(SqliteDataReader reader, int ordinal)
        => reader.IsDBNull(ordinal) ? null : ParseDate(reader.GetString(ordinal));

    private static string? FormatDate(DateTimeOffset? value)
        => value?.ToString("O", CultureInfo.InvariantCulture);

    private static DateTimeOffset ParseDate(string value)
        => DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
}
