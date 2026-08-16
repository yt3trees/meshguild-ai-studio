using System.Globalization;
using Microsoft.Data.Sqlite;
using WorkAgents.Core.Abstractions;
using WorkAgents.Core.Missions;

namespace WorkAgents.Infrastructure.Stores;

public sealed class SqliteMissionWorkspaceStore : IMissionWorkspaceStore
{
    private readonly string _connectionString;
    private readonly Task _initialization;

    public SqliteMissionWorkspaceStore(string databasePath)
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

    public async Task<MissionWorkspaceRecord?> GetAsync(string missionId, CancellationToken ct = default)
    {
        ValidateMissionId(missionId);
        await EnsureInitializedAsync(ct);

        await using var connection = await OpenConnectionAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT mission_id, workspace_key, prepared_at, deleted_at
            FROM mission_workspaces
            WHERE mission_id = $mission_id;
            """;
        command.Parameters.AddWithValue("$mission_id", missionId);

        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
        {
            return null;
        }

        return new MissionWorkspaceRecord
        {
            MissionId = reader.GetString(0),
            WorkspaceKey = reader.GetString(1),
            PreparedAtUtc = ParseDate(reader.GetString(2)),
            DeletedAtUtc = reader.IsDBNull(3) ? null : ParseDate(reader.GetString(3)),
        };
    }

    public async Task RecordPreparedAsync(MissionWorkspaceRecord record, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        ValidateMissionId(record.MissionId);
        ValidateWorkspaceKey(record.WorkspaceKey, record.MissionId);
        await EnsureInitializedAsync(ct);

        await using var connection = await OpenConnectionAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO mission_workspaces (mission_id, workspace_key, prepared_at, deleted_at)
            VALUES ($mission_id, $workspace_key, $prepared_at, NULL)
            ON CONFLICT(mission_id) DO UPDATE SET
                workspace_key = excluded.workspace_key,
                prepared_at = excluded.prepared_at,
                deleted_at = NULL;
            """;
        command.Parameters.AddWithValue("$mission_id", record.MissionId);
        command.Parameters.AddWithValue("$workspace_key", record.WorkspaceKey);
        command.Parameters.AddWithValue("$prepared_at", FormatDate(record.PreparedAtUtc));
        await command.ExecuteNonQueryAsync(ct);
    }

    public async Task MarkDeletedAsync(string missionId, DateTimeOffset deletedAtUtc, CancellationToken ct = default)
    {
        ValidateMissionId(missionId);
        await EnsureInitializedAsync(ct);

        await using var connection = await OpenConnectionAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE mission_workspaces
            SET deleted_at = $deleted_at
            WHERE mission_id = $mission_id;
            """;
        command.Parameters.AddWithValue("$mission_id", missionId);
        command.Parameters.AddWithValue("$deleted_at", FormatDate(deletedAtUtc));
        await command.ExecuteNonQueryAsync(ct);
    }

    private async Task InitializeAsync()
    {
        await using var connection = await OpenConnectionAsync(CancellationToken.None);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS mission_workspaces (
                mission_id TEXT NOT NULL PRIMARY KEY,
                workspace_key TEXT NOT NULL,
                prepared_at TEXT NOT NULL,
                deleted_at TEXT NULL
            );
            """;
        await command.ExecuteNonQueryAsync();
    }

    private async Task EnsureInitializedAsync(CancellationToken ct)
        => await _initialization.WaitAsync(ct);

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

    private static void ValidateMissionId(string missionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(missionId);
        if (missionId is "." or ".."
            || missionId.Contains(Path.DirectorySeparatorChar)
            || missionId.Contains(Path.AltDirectorySeparatorChar)
            || Path.IsPathRooted(missionId)
            || missionId.Contains(':'))
        {
            throw new ArgumentException("Mission ID is not a safe path segment.", nameof(missionId));
        }
    }

    private static void ValidateWorkspaceKey(string workspaceKey, string missionId)
    {
        if (string.IsNullOrWhiteSpace(workspaceKey)
            || Path.IsPathRooted(workspaceKey)
            || workspaceKey.Contains('\\')
            || workspaceKey.Contains("..", StringComparison.Ordinal)
            || !string.Equals(workspaceKey, $"missions/{missionId}/work", StringComparison.Ordinal))
        {
            throw new ArgumentException("Mission workspace key is invalid.", nameof(workspaceKey));
        }
    }

    private static string FormatDate(DateTimeOffset value)
        => value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    private static DateTimeOffset ParseDate(string value)
        => DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
}
