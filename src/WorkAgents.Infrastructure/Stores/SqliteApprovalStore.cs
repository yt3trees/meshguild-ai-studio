using System.Globalization;
using Microsoft.Data.Sqlite;
using WorkAgents.Core;
using WorkAgents.Core.Abstractions;

namespace WorkAgents.Infrastructure.Stores;

/// <summary>Localプロファイル用のSQLite承認ストア。</summary>
public sealed class SqliteApprovalStore : IApprovalStore
{
    private readonly string _connectionString;
    private readonly ISecretRedactor? _redactor;
    private readonly Task _initialization;

    public SqliteApprovalStore(string databasePath, ISecretRedactor? redactor = null)
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
        _redactor = redactor;
        _initialization = InitializeAsync();
    }

    public async Task CreateAsync(ApprovalRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Status != ApprovalStatus.Pending)
        {
            throw new ArgumentException("New approval requests must be pending.", nameof(request));
        }

        await EnsureInitializedAsync(ct);

        await using var connection = await OpenConnectionAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO approvals (
                approval_id, run_id, tool, args_summary, status, title,
                created_at, expires_at, decided_by, decided_at, decision_reason,
                mission_id, agent_instance_id, node_run_id, iteration_id)
            VALUES ($approval_id, $run_id, $tool, $args_summary, $status, $title,
                $created_at, $expires_at, $decided_by, $decided_at, $decision_reason,
                $mission_id, $agent_instance_id, $node_run_id, $iteration_id);
            """;
        await AddRequestParametersAsync(command, request, ct);
        await command.ExecuteNonQueryAsync(ct);
    }

    public async Task<ApprovalRequest?> GetAsync(string approvalId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(approvalId);
        await EnsureInitializedAsync(ct);

        await using var connection = await OpenConnectionAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT approval_id, run_id, tool, args_summary, status, title,
                   created_at, expires_at, decided_by, decided_at, decision_reason,
                   mission_id, agent_instance_id, node_run_id, iteration_id
            FROM approvals
            WHERE approval_id = $approval_id;
            """;
        command.Parameters.AddWithValue("$approval_id", approvalId);

        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? ReadRequest(reader) : null;
    }

    public async Task<IReadOnlyList<ApprovalRequest>> ListPendingAsync(
        string? runId = null,
        CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        await using var connection = await OpenConnectionAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = string.IsNullOrWhiteSpace(runId)
            ? """
              SELECT approval_id, run_id, tool, args_summary, status, title,
                     created_at, expires_at, decided_by, decided_at, decision_reason,
                     mission_id, agent_instance_id, node_run_id, iteration_id
              FROM approvals
              WHERE status = $pending
              ORDER BY created_at ASC;
              """
            : """
              SELECT approval_id, run_id, tool, args_summary, status, title,
                     created_at, expires_at, decided_by, decided_at, decision_reason,
                     mission_id, agent_instance_id, node_run_id, iteration_id
              FROM approvals
              WHERE status = $pending AND run_id = $run_id
              ORDER BY created_at ASC;
              """;
        command.Parameters.AddWithValue("$pending", (int)ApprovalStatus.Pending);
        if (!string.IsNullOrWhiteSpace(runId))
        {
            command.Parameters.AddWithValue("$run_id", runId);
        }

        var requests = new List<ApprovalRequest>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            requests.Add(ReadRequest(reader));
        }

        return requests;
    }

    public async Task<bool> TryDecideAsync(
        string approvalId,
        ApprovalStatus status,
        string decidedBy,
        string? reason = null,
        DateTimeOffset? decidedAt = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(approvalId);
        ArgumentException.ThrowIfNullOrWhiteSpace(decidedBy);
        ApprovalStatusMachine.EnsureTransition(ApprovalStatus.Pending, status);
        await EnsureInitializedAsync(ct);

        var decisionTime = decidedAt ?? DateTimeOffset.UtcNow;
        await using var connection = await OpenConnectionAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE approvals
            SET status = $status,
                decided_by = $decided_by,
                decided_at = $decided_at,
                decision_reason = $decision_reason
            WHERE approval_id = $approval_id
              AND status = $pending
              AND ($status = $rejected OR expires_at > $decided_at);
            """;
        command.Parameters.AddWithValue("$status", (int)status);
        command.Parameters.AddWithValue("$rejected", (int)ApprovalStatus.Rejected);
        command.Parameters.AddWithValue("$pending", (int)ApprovalStatus.Pending);
        command.Parameters.AddWithValue("$approval_id", approvalId);
        command.Parameters.AddWithValue("$decided_by", decidedBy);
        command.Parameters.AddWithValue("$decided_at", FormatDate(decisionTime));
        command.Parameters.AddWithValue("$decision_reason", (object?)(_redactor is null || reason is null ? reason : await _redactor.RedactAsync(reason, ct)) ?? DBNull.Value);
        return await command.ExecuteNonQueryAsync(ct) == 1;
    }

    private async Task InitializeAsync()
    {
        await using var connection = await OpenConnectionAsync(CancellationToken.None);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA journal_mode = WAL;
            CREATE TABLE IF NOT EXISTS approvals (
                approval_id TEXT NOT NULL PRIMARY KEY,
                run_id TEXT NOT NULL,
                tool TEXT NOT NULL,
                args_summary TEXT NOT NULL,
                status INTEGER NOT NULL,
                created_at TEXT NOT NULL,
                expires_at TEXT NOT NULL,
                decided_by TEXT NULL,
                decided_at TEXT NULL,
                decision_reason TEXT NULL
            );
            CREATE INDEX IF NOT EXISTS ix_approvals_status_created_at
                ON approvals(status, created_at ASC);
            CREATE INDEX IF NOT EXISTS ix_approvals_run_id_status
                ON approvals(run_id, status);
            """;
        await command.ExecuteNonQueryAsync();

        // M3.5: title 列を既存 DB へ後付け追加(冪等)。
        if (!await ColumnExistsAsync(connection, "approvals", "title"))
        {
            await using var alter = connection.CreateCommand();
            alter.CommandText = "ALTER TABLE approvals ADD COLUMN title TEXT NOT NULL DEFAULT '';";
            await alter.ExecuteNonQueryAsync();
        }

        // T031: ミッション経路の承認要求向けの列を後付け追加(冪等)。
        foreach (var column in new[] { "mission_id", "agent_instance_id", "node_run_id", "iteration_id" })
        {
            if (!await ColumnExistsAsync(connection, "approvals", column))
            {
                await using var alter = connection.CreateCommand();
                alter.CommandText = $"ALTER TABLE approvals ADD COLUMN {column} TEXT NULL;";
                await alter.ExecuteNonQueryAsync();
            }
        }
    }

    private static async Task<bool> ColumnExistsAsync(SqliteConnection connection, string table, string column)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM pragma_table_info($table) WHERE name = $column;";
        command.Parameters.AddWithValue("$table", table);
        command.Parameters.AddWithValue("$column", column);
        var value = await command.ExecuteScalarAsync();
        return Convert.ToInt32(value, CultureInfo.InvariantCulture) > 0;
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

    private async Task AddRequestParametersAsync(SqliteCommand command, ApprovalRequest request, CancellationToken ct)
    {
        command.Parameters.AddWithValue("$approval_id", request.ApprovalId);
        command.Parameters.AddWithValue("$run_id", request.RunId);
        command.Parameters.AddWithValue("$tool", request.Tool);
        command.Parameters.AddWithValue("$args_summary", _redactor is null ? request.ArgsSummary : await _redactor.RedactAsync(request.ArgsSummary, ct));
        command.Parameters.AddWithValue("$status", (int)request.Status);
        command.Parameters.AddWithValue("$title", request.Title ?? string.Empty);
        command.Parameters.AddWithValue("$created_at", FormatDate(request.CreatedAt));
        command.Parameters.AddWithValue("$expires_at", FormatDate(request.ExpiresAt));
        command.Parameters.AddWithValue("$decided_by", (object?)request.DecidedBy ?? DBNull.Value);
        command.Parameters.AddWithValue("$decided_at", (object?)FormatDate(request.DecidedAt) ?? DBNull.Value);
        command.Parameters.AddWithValue("$decision_reason", (object?)request.DecisionReason ?? DBNull.Value);
        command.Parameters.AddWithValue("$mission_id", (object?)request.MissionId ?? DBNull.Value);
        command.Parameters.AddWithValue("$agent_instance_id", (object?)request.AgentInstanceId ?? DBNull.Value);
        command.Parameters.AddWithValue("$node_run_id", (object?)request.NodeRunId ?? DBNull.Value);
        command.Parameters.AddWithValue("$iteration_id", (object?)request.IterationId ?? DBNull.Value);
    }

    private static ApprovalRequest ReadRequest(SqliteDataReader reader)
    {
        return new ApprovalRequest
        {
            ApprovalId = reader.GetString(0),
            RunId = reader.GetString(1),
            Tool = reader.GetString(2),
            ArgsSummary = reader.GetString(3),
            Status = (ApprovalStatus)reader.GetInt32(4),
            Title = ReadNullableString(reader, 5) ?? string.Empty,
            CreatedAt = ParseDate(reader.GetString(6)),
            ExpiresAt = ParseDate(reader.GetString(7)),
            DecidedBy = ReadNullableString(reader, 8),
            DecidedAt = ReadNullableDate(reader, 9),
            DecisionReason = ReadNullableString(reader, 10),
            MissionId = ReadNullableString(reader, 11),
            AgentInstanceId = ReadNullableString(reader, 12),
            NodeRunId = ReadNullableString(reader, 13),
            IterationId = ReadNullableString(reader, 14),
        };
    }

    private static string? ReadNullableString(SqliteDataReader reader, int ordinal)
    {
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }

    private static DateTimeOffset? ReadNullableDate(SqliteDataReader reader, int ordinal)
    {
        return reader.IsDBNull(ordinal) ? null : ParseDate(reader.GetString(ordinal));
    }

    private static string FormatDate(DateTimeOffset value)
        => value.ToString("O", CultureInfo.InvariantCulture);

    private static string? FormatDate(DateTimeOffset? value)
        => value?.ToString("O", CultureInfo.InvariantCulture);

    private static DateTimeOffset ParseDate(string value)
        => DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
}
