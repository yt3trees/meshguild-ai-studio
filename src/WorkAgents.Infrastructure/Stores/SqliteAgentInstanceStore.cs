using System.Globalization;
using Microsoft.Data.Sqlite;
using WorkAgents.Core.Abstractions;
using WorkAgents.Core.Missions;

namespace WorkAgents.Infrastructure.Stores;

/// <summary>Localプロファイル用のSQLiteエージェントインスタンスストア (agent_instances テーブル、T029)。</summary>
public sealed class SqliteAgentInstanceStore : IAgentInstanceStore
{
    private readonly string _connectionString;
    private readonly Task _initialization;

    public SqliteAgentInstanceStore(string databasePath)
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

    public async Task CreateAsync(AgentInstance instance, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(instance);
        await EnsureInitializedAsync(ct);

        await using var connection = await OpenConnectionAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO agent_instances (
                instance_id, mission_id, agent_name, role, instance_no, state,
                awaiting_instance_id, joined_at, left_at, join_reason, leave_reason, model_name)
            VALUES (
                $instance_id, $mission_id, $agent_name, $role, $instance_no, $state,
                $awaiting_instance_id, $joined_at, $left_at, $join_reason, $leave_reason, $model_name);
            """;
        command.Parameters.AddWithValue("$instance_id", instance.InstanceId);
        command.Parameters.AddWithValue("$mission_id", instance.MissionId);
        command.Parameters.AddWithValue("$agent_name", instance.AgentName);
        command.Parameters.AddWithValue("$role", instance.Role.ToString());
        command.Parameters.AddWithValue("$instance_no", instance.InstanceNo);
        command.Parameters.AddWithValue("$state", instance.State.ToString());
        command.Parameters.AddWithValue("$awaiting_instance_id", (object?)instance.AwaitingInstanceId ?? DBNull.Value);
        command.Parameters.AddWithValue("$joined_at", FormatDate(instance.JoinedAt));
        command.Parameters.AddWithValue("$left_at", (object?)FormatDate(instance.LeftAt) ?? DBNull.Value);
        command.Parameters.AddWithValue("$join_reason", (object?)instance.JoinReason ?? DBNull.Value);
        command.Parameters.AddWithValue("$leave_reason", (object?)instance.LeaveReason ?? DBNull.Value);
        command.Parameters.AddWithValue("$model_name", (object?)instance.ModelName ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(ct);
    }

    public async Task<AgentInstance?> GetAsync(string instanceId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceId);
        await EnsureInitializedAsync(ct);

        await using var connection = await OpenConnectionAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = SelectSql + " WHERE instance_id = $instance_id;";
        command.Parameters.AddWithValue("$instance_id", instanceId);

        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? ReadInstance(reader) : null;
    }

    public async Task<IReadOnlyList<AgentInstance>> ListByMissionAsync(string missionId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(missionId);
        await EnsureInitializedAsync(ct);

        await using var connection = await OpenConnectionAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = SelectSql + " WHERE mission_id = $mission_id ORDER BY joined_at ASC;";
        command.Parameters.AddWithValue("$mission_id", missionId);

        var instances = new List<AgentInstance>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            instances.Add(ReadInstance(reader));
        }
        return instances;
    }

    public async Task SetStateAsync(
        string instanceId,
        AgentInstanceState state,
        string? awaitingInstanceId = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceId);
        await EnsureInitializedAsync(ct);

        await using var connection = await OpenConnectionAsync(ct);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(ct);

        await using var readCommand = connection.CreateCommand();
        readCommand.Transaction = transaction;
        readCommand.CommandText = "SELECT state FROM agent_instances WHERE instance_id = $instance_id;";
        readCommand.Parameters.AddWithValue("$instance_id", instanceId);
        var currentValue = await readCommand.ExecuteScalarAsync(ct)
            ?? throw new KeyNotFoundException($"Agent instance not found: '{instanceId}'.");
        var current = Enum.Parse<AgentInstanceState>((string)currentValue);
        AgentInstanceStateMachine.EnsureTransition(current, state);

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE agent_instances SET state = $state, awaiting_instance_id = $awaiting_instance_id
            WHERE instance_id = $instance_id;
            """;
        command.Parameters.AddWithValue("$state", state.ToString());
        command.Parameters.AddWithValue("$awaiting_instance_id", (object?)awaitingInstanceId ?? DBNull.Value);
        command.Parameters.AddWithValue("$instance_id", instanceId);
        await command.ExecuteNonQueryAsync(ct);
        await transaction.CommitAsync(ct);
    }

    public async Task SetLeftAsync(string instanceId, string leaveReason, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceId);
        await EnsureInitializedAsync(ct);

        await using var connection = await OpenConnectionAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE agent_instances SET left_at = $left_at, leave_reason = $leave_reason
            WHERE instance_id = $instance_id;
            """;
        command.Parameters.AddWithValue("$left_at", FormatDate(DateTimeOffset.UtcNow));
        command.Parameters.AddWithValue("$leave_reason", leaveReason);
        command.Parameters.AddWithValue("$instance_id", instanceId);
        await command.ExecuteNonQueryAsync(ct);
    }

    private const string SelectSql = """
        SELECT instance_id, mission_id, agent_name, role, instance_no, state,
               awaiting_instance_id, joined_at, left_at, join_reason, leave_reason, model_name
        FROM agent_instances
        """;

    private async Task InitializeAsync()
    {
        await using var connection = await OpenConnectionAsync(CancellationToken.None);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA journal_mode = WAL;
            CREATE TABLE IF NOT EXISTS agent_instances (
                instance_id TEXT NOT NULL PRIMARY KEY,
                mission_id TEXT NOT NULL,
                agent_name TEXT NOT NULL,
                role TEXT NOT NULL,
                instance_no INTEGER NOT NULL,
                state TEXT NOT NULL,
                awaiting_instance_id TEXT NULL,
                joined_at TEXT NOT NULL,
                left_at TEXT NULL,
                join_reason TEXT NULL,
                leave_reason TEXT NULL,
                model_name TEXT NULL
            );
            CREATE INDEX IF NOT EXISTS ix_agent_instances_mission ON agent_instances(mission_id, state);
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

    private static AgentInstance ReadInstance(SqliteDataReader reader)
    {
        return new AgentInstance
        {
            InstanceId = reader.GetString(0),
            MissionId = reader.GetString(1),
            AgentName = reader.GetString(2),
            Role = Enum.Parse<AgentInstanceRole>(reader.GetString(3)),
            InstanceNo = reader.GetInt32(4),
            State = Enum.Parse<AgentInstanceState>(reader.GetString(5)),
            AwaitingInstanceId = ReadNullableString(reader, 6),
            JoinedAt = ParseDate(reader.GetString(7)),
            LeftAt = ReadNullableString(reader, 8) is { } l ? ParseDate(l) : null,
            JoinReason = ReadNullableString(reader, 9),
            LeaveReason = ReadNullableString(reader, 10),
            ModelName = ReadNullableString(reader, 11),
        };
    }

    private static string? ReadNullableString(SqliteDataReader reader, int ordinal)
        => reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);

    private static string FormatDate(DateTimeOffset value)
        => value.ToString("O", CultureInfo.InvariantCulture);

    private static string? FormatDate(DateTimeOffset? value)
        => value?.ToString("O", CultureInfo.InvariantCulture);

    private static DateTimeOffset ParseDate(string value)
        => DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
}
