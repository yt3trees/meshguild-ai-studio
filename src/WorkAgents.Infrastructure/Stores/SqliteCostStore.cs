using System.Globalization;
using Microsoft.Data.Sqlite;
using WorkAgents.Core;
using WorkAgents.Core.Abstractions;

namespace WorkAgents.Infrastructure.Stores;

/// <summary>Localプロファイル用のSQLiteコストストア(第6章 `costs`)。</summary>
public sealed class SqliteCostStore : ICostStore
{
    private readonly string _connectionString;
    private readonly Task _initialization;

    public SqliteCostStore(string databasePath)
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
        _initialization = InitializeAsync();
    }

    public async Task RecordAsync(CostRecord record, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        await EnsureInitializedAsync(ct);

        await using var connection = await OpenConnectionAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO costs (
                run_id, thread_id, agent_name, model_name, provider,
                input_tokens, output_tokens, total_tokens, created_at,
                cost_record_id, mission_id, agent_instance_id, node_run_id, iteration_id, estimated_cost_usd)
            VALUES (
                $run_id, $thread_id, $agent_name, $model_name, $provider,
                $input_tokens, $output_tokens, $total_tokens, $created_at,
                $cost_record_id, $mission_id, $agent_instance_id, $node_run_id, $iteration_id, $estimated_cost_usd);
            """;
        command.Parameters.AddWithValue("$run_id", (object?)record.RunId ?? DBNull.Value);
        command.Parameters.AddWithValue("$thread_id", (object?)record.ThreadId ?? DBNull.Value);
        command.Parameters.AddWithValue("$agent_name", record.AgentName);
        command.Parameters.AddWithValue("$model_name", (object?)record.ModelName ?? DBNull.Value);
        command.Parameters.AddWithValue("$provider", (object?)record.Provider ?? DBNull.Value);
        command.Parameters.AddWithValue("$input_tokens", (object?)record.InputTokens ?? DBNull.Value);
        command.Parameters.AddWithValue("$output_tokens", (object?)record.OutputTokens ?? DBNull.Value);
        command.Parameters.AddWithValue("$total_tokens", (object?)record.TotalTokens ?? DBNull.Value);
        command.Parameters.AddWithValue("$created_at", record.CreatedAt.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$cost_record_id", (object?)record.CostRecordId ?? DBNull.Value);
        command.Parameters.AddWithValue("$mission_id", (object?)record.MissionId ?? DBNull.Value);
        command.Parameters.AddWithValue("$agent_instance_id", (object?)record.AgentInstanceId ?? DBNull.Value);
        command.Parameters.AddWithValue("$node_run_id", (object?)record.NodeRunId ?? DBNull.Value);
        command.Parameters.AddWithValue("$iteration_id", (object?)record.IterationId ?? DBNull.Value);
        command.Parameters.AddWithValue("$estimated_cost_usd", (object?)record.EstimatedCostUsd ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(ct);
    }

    public async Task<IReadOnlyList<CostRecord>> ListAsync(DateTimeOffset since, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        await using var connection = await OpenConnectionAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT run_id, thread_id, agent_name, model_name, provider,
                   input_tokens, output_tokens, total_tokens, created_at,
                   cost_record_id, mission_id, agent_instance_id, node_run_id, iteration_id, estimated_cost_usd
            FROM costs
            WHERE created_at >= $since
            ORDER BY id;
            """;
        command.Parameters.AddWithValue("$since", since.ToString("O", CultureInfo.InvariantCulture));

        var records = new List<CostRecord>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            records.Add(Read(reader));
        }
        return records;
    }

    private async Task InitializeAsync()
    {
        await using var connection = await OpenConnectionAsync(CancellationToken.None);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA journal_mode = WAL;
            CREATE TABLE IF NOT EXISTS costs (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                run_id TEXT NULL,
                thread_id TEXT NULL,
                agent_name TEXT NOT NULL,
                model_name TEXT NULL,
                provider TEXT NULL,
                input_tokens INTEGER NULL,
                output_tokens INTEGER NULL,
                total_tokens INTEGER NULL,
                created_at TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS ix_costs_created_at ON costs(created_at);
            CREATE INDEX IF NOT EXISTS ix_costs_agent_name ON costs(agent_name, created_at);
            """;
        await command.ExecuteNonQueryAsync();

        // T032: ミッション経路のコスト記録向けの列を後付け追加(冪等)。
        foreach (var column in new[] { "cost_record_id", "mission_id", "agent_instance_id", "node_run_id", "iteration_id" })
        {
            if (!await ColumnExistsAsync(connection, "costs", column))
            {
                await using var alter = connection.CreateCommand();
                alter.CommandText = $"ALTER TABLE costs ADD COLUMN {column} TEXT NULL;";
                await alter.ExecuteNonQueryAsync();
            }
        }
        if (!await ColumnExistsAsync(connection, "costs", "estimated_cost_usd"))
        {
            await using var alter = connection.CreateCommand();
            alter.CommandText = "ALTER TABLE costs ADD COLUMN estimated_cost_usd REAL NULL;";
            await alter.ExecuteNonQueryAsync();
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

    private async Task EnsureInitializedAsync(CancellationToken ct) => await _initialization.WaitAsync(ct);

    private async Task<SqliteConnection> OpenConnectionAsync(CancellationToken ct)
    {
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(ct);
        return connection;
    }

    private static CostRecord Read(SqliteDataReader reader) => new()
    {
        RunId = reader.IsDBNull(0) ? null : reader.GetString(0),
        ThreadId = reader.IsDBNull(1) ? null : reader.GetString(1),
        AgentName = reader.GetString(2),
        ModelName = reader.IsDBNull(3) ? null : reader.GetString(3),
        Provider = reader.IsDBNull(4) ? null : reader.GetString(4),
        InputTokens = reader.IsDBNull(5) ? null : reader.GetInt64(5),
        OutputTokens = reader.IsDBNull(6) ? null : reader.GetInt64(6),
        TotalTokens = reader.IsDBNull(7) ? null : reader.GetInt64(7),
        CreatedAt = DateTimeOffset.Parse(reader.GetString(8), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
        CostRecordId = reader.IsDBNull(9) ? null : reader.GetString(9),
        MissionId = reader.IsDBNull(10) ? null : reader.GetString(10),
        AgentInstanceId = reader.IsDBNull(11) ? null : reader.GetString(11),
        NodeRunId = reader.IsDBNull(12) ? null : reader.GetString(12),
        IterationId = reader.IsDBNull(13) ? null : reader.GetString(13),
        EstimatedCostUsd = reader.IsDBNull(14) ? null : reader.GetDouble(14),
    };
}
