using System.Globalization;
using Microsoft.Data.Sqlite;
using WorkAgents.Core.Abstractions;
using WorkAgents.Core.Missions;

namespace WorkAgents.Infrastructure.Stores;

/// <summary>Localプロファイル用のSQLite待機列ストア (mission_queue テーブル、T030)。FIFO の position 採番と昇格を扱う。</summary>
public sealed class SqliteMissionQueueStore : IMissionQueueStore
{
    private readonly string _connectionString;
    private readonly Task _initialization;

    public SqliteMissionQueueStore(string databasePath)
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

    public async Task<int> EnqueueAsync(string missionId, MissionQueuedReason reason, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(missionId);
        await EnsureInitializedAsync(ct);

        await using var connection = await OpenConnectionAsync(ct);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(ct);

        await using var maxCommand = connection.CreateCommand();
        maxCommand.Transaction = transaction;
        maxCommand.CommandText = "SELECT COALESCE(MAX(position), 0) + 1 FROM mission_queue;";
        var position = Convert.ToInt32(await maxCommand.ExecuteScalarAsync(ct), CultureInfo.InvariantCulture);

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO mission_queue (mission_id, position, reason, enqueued_at)
            VALUES ($mission_id, $position, $reason, $enqueued_at);
            """;
        command.Parameters.AddWithValue("$mission_id", missionId);
        command.Parameters.AddWithValue("$position", position);
        command.Parameters.AddWithValue("$reason", reason.ToString());
        command.Parameters.AddWithValue("$enqueued_at", DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        await command.ExecuteNonQueryAsync(ct);
        await transaction.CommitAsync(ct);
        return position;
    }

    public async Task<IReadOnlyList<MissionQueueEntry>> ListAsync(CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        await using var connection = await OpenConnectionAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT mission_id, position, reason, enqueued_at FROM mission_queue ORDER BY position ASC;
            """;

        var entries = new List<MissionQueueEntry>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            entries.Add(ReadEntry(reader));
        }
        return entries;
    }

    public async Task<MissionQueueEntry?> DequeueAsync(CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        await using var connection = await OpenConnectionAsync(ct);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(ct);

        await using var selectCommand = connection.CreateCommand();
        selectCommand.Transaction = transaction;
        selectCommand.CommandText = """
            SELECT mission_id, position, reason, enqueued_at FROM mission_queue ORDER BY position ASC LIMIT 1;
            """;
        MissionQueueEntry? entry = null;
        await using (var reader = await selectCommand.ExecuteReaderAsync(ct))
        {
            if (await reader.ReadAsync(ct))
            {
                entry = ReadEntry(reader);
            }
        }

        if (entry is null)
        {
            await transaction.CommitAsync(ct);
            return null;
        }

        await using var deleteCommand = connection.CreateCommand();
        deleteCommand.Transaction = transaction;
        deleteCommand.CommandText = "DELETE FROM mission_queue WHERE mission_id = $mission_id;";
        deleteCommand.Parameters.AddWithValue("$mission_id", entry.MissionId);
        await deleteCommand.ExecuteNonQueryAsync(ct);
        await transaction.CommitAsync(ct);
        return entry;
    }

    public async Task RemoveAsync(string missionId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(missionId);
        await EnsureInitializedAsync(ct);

        await using var connection = await OpenConnectionAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM mission_queue WHERE mission_id = $mission_id;";
        command.Parameters.AddWithValue("$mission_id", missionId);
        await command.ExecuteNonQueryAsync(ct);
    }

    private async Task InitializeAsync()
    {
        await using var connection = await OpenConnectionAsync(CancellationToken.None);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA journal_mode = WAL;
            CREATE TABLE IF NOT EXISTS mission_queue (
                mission_id TEXT NOT NULL PRIMARY KEY,
                position INTEGER NOT NULL,
                reason TEXT NOT NULL,
                enqueued_at TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS ix_mission_queue_position ON mission_queue(position ASC);
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

    private static MissionQueueEntry ReadEntry(SqliteDataReader reader)
    {
        return new MissionQueueEntry
        {
            MissionId = reader.GetString(0),
            Position = reader.GetInt32(1),
            Reason = Enum.Parse<MissionQueuedReason>(reader.GetString(2)),
            EnqueuedAt = DateTimeOffset.Parse(reader.GetString(3), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
        };
    }
}
