using System.Globalization;
using Microsoft.Data.Sqlite;
using WorkAgents.Core.Abstractions;
using WorkAgents.Core.Missions;

namespace WorkAgents.Infrastructure.Stores;

/// <summary>SQLite checkpoint store.</summary>
public sealed class SqliteCheckpointStore : ICheckpointStore
{
    private readonly string _connectionString;
    private readonly Task _initialization;

    public SqliteCheckpointStore(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        var fullPath = Path.GetFullPath(databasePath);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        _connectionString = new SqliteConnectionStringBuilder { DataSource = fullPath, Mode = SqliteOpenMode.ReadWriteCreate, Cache = SqliteCacheMode.Shared, Pooling = false }.ToString();
        _initialization = InitializeAsync();
    }

    public async Task CreateAsync(Checkpoint checkpoint, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        await EnsureInitializedAsync(ct);
        await using var connection = await OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO checkpoints (
                checkpoint_id, mission_id, boundary_kind, node_run_id, iteration_id, last_message_seq,
                state_json, workspace_path, workspace_restorable, created_at)
            VALUES ($id, $mission, $boundary, $node, $iteration, $seq, $state, $workspace, $restorable, $created);
            """;
        command.Parameters.AddWithValue("$id", checkpoint.CheckpointId);
        command.Parameters.AddWithValue("$mission", checkpoint.MissionId);
        command.Parameters.AddWithValue("$boundary", checkpoint.BoundaryKind.ToString());
        command.Parameters.AddWithValue("$node", (object?)checkpoint.NodeRunId ?? DBNull.Value);
        command.Parameters.AddWithValue("$iteration", (object?)checkpoint.IterationId ?? DBNull.Value);
        command.Parameters.AddWithValue("$seq", checkpoint.LastMessageSeq);
        command.Parameters.AddWithValue("$state", checkpoint.StateJson);
        command.Parameters.AddWithValue("$workspace", (object?)checkpoint.WorkspacePath ?? DBNull.Value);
        command.Parameters.AddWithValue("$restorable", checkpoint.WorkspaceRestorable ? 1 : 0);
        command.Parameters.AddWithValue("$created", Format(checkpoint.CreatedAt));
        await command.ExecuteNonQueryAsync(ct);
    }

    public async Task<Checkpoint?> GetLatestAsync(string missionId, CancellationToken ct = default)
    {
        var list = await ListAsync(missionId, ct);
        return list.OrderByDescending(checkpoint => checkpoint.CreatedAt).FirstOrDefault();
    }

    public async Task<IReadOnlyList<Checkpoint>> ListAsync(string missionId, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        await using var connection = await OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT checkpoint_id, mission_id, boundary_kind, node_run_id, iteration_id, last_message_seq,
                   state_json, workspace_path, workspace_restorable, created_at
            FROM checkpoints WHERE mission_id = $mission ORDER BY created_at ASC;
            """;
        command.Parameters.AddWithValue("$mission", missionId);
        var list = new List<Checkpoint>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            list.Add(new Checkpoint
            {
                CheckpointId = reader.GetString(0),
                MissionId = reader.GetString(1),
                BoundaryKind = Enum.Parse<CheckpointBoundaryKind>(reader.GetString(2)),
                NodeRunId = reader.IsDBNull(3) ? null : reader.GetString(3),
                IterationId = reader.IsDBNull(4) ? null : reader.GetString(4),
                LastMessageSeq = reader.GetInt64(5),
                StateJson = reader.GetString(6),
                WorkspacePath = reader.IsDBNull(7) ? null : reader.GetString(7),
                WorkspaceRestorable = reader.GetInt32(8) != 0,
                CreatedAt = Parse(reader.GetString(9)),
            });
        }
        return list;
    }

    private async Task InitializeAsync()
    {
        await using var connection = await OpenAsync(CancellationToken.None);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS checkpoints (
                checkpoint_id TEXT NOT NULL PRIMARY KEY,
                mission_id TEXT NOT NULL,
                boundary_kind TEXT NOT NULL,
                node_run_id TEXT NULL,
                iteration_id TEXT NULL,
                last_message_seq INTEGER NOT NULL,
                state_json TEXT NOT NULL,
                workspace_path TEXT NULL,
                workspace_restorable INTEGER NOT NULL,
                created_at TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS ix_checkpoints_mission ON checkpoints(mission_id, created_at DESC);
            """;
        await command.ExecuteNonQueryAsync();
    }
    private async Task EnsureInitializedAsync(CancellationToken ct) => await _initialization.WaitAsync(ct);
    private async Task<SqliteConnection> OpenAsync(CancellationToken ct)
    {
        var connection = new SqliteConnection(_connectionString);
        try { await connection.OpenAsync(ct); return connection; }
        catch { await connection.DisposeAsync(); throw; }
    }
    private static string Format(DateTimeOffset value) => value.ToString("O", CultureInfo.InvariantCulture);
    private static DateTimeOffset Parse(string value) => DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
}
