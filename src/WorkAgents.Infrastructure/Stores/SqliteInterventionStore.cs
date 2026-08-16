using System.Globalization;
using Microsoft.Data.Sqlite;
using WorkAgents.Core.Abstractions;
using WorkAgents.Core.Missions;

namespace WorkAgents.Infrastructure.Stores;

/// <summary>SQLite intervention store. Unapplied instructions are consumed by the next turn.</summary>
public sealed class SqliteInterventionStore : IInterventionStore
{
    private readonly string _connectionString;
    private readonly Task _initialization;

    public SqliteInterventionStore(string databasePath)
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

    public async Task CreateAsync(Intervention intervention, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(intervention);
        await EnsureInitializedAsync(ct);
        await using var connection = await OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO interventions (
                intervention_id, mission_id, message_id, target_instance_id, body,
                created_at, applied_at, applied_to_message_id)
            VALUES ($id, $mission, $message, $target, $body, $created, $applied, $applied_message);
            """;
        command.Parameters.AddWithValue("$id", intervention.InterventionId);
        command.Parameters.AddWithValue("$mission", intervention.MissionId);
        command.Parameters.AddWithValue("$message", intervention.MessageId);
        command.Parameters.AddWithValue("$target", (object?)intervention.TargetInstanceId ?? DBNull.Value);
        command.Parameters.AddWithValue("$body", intervention.Body);
        command.Parameters.AddWithValue("$created", Format(intervention.CreatedAt));
        command.Parameters.AddWithValue("$applied", (object?)Format(intervention.AppliedAt) ?? DBNull.Value);
        command.Parameters.AddWithValue("$applied_message", (object?)intervention.AppliedToMessageId ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(ct);
    }

    public async Task<IReadOnlyList<Intervention>> ListUnappliedAsync(
        string missionId,
        string? targetInstanceId = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(missionId);
        await EnsureInitializedAsync(ct);
        await using var connection = await OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT intervention_id, mission_id, message_id, target_instance_id, body,
                   created_at, applied_at, applied_to_message_id
            FROM interventions
            WHERE mission_id = $mission AND applied_at IS NULL
              AND (target_instance_id IS NULL OR target_instance_id = $target)
            ORDER BY created_at ASC;
            """;
        command.Parameters.AddWithValue("$mission", missionId);
        command.Parameters.AddWithValue("$target", (object?)targetInstanceId ?? DBNull.Value);
        var result = new List<Intervention>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            result.Add(Read(reader));
        }
        return result;
    }

    public async Task MarkAppliedAsync(string interventionId, string appliedToMessageId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(interventionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(appliedToMessageId);
        await EnsureInitializedAsync(ct);
        await using var connection = await OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE interventions
            SET applied_at = $applied, applied_to_message_id = $message
            WHERE intervention_id = $id AND applied_at IS NULL;
            """;
        command.Parameters.AddWithValue("$applied", Format(DateTimeOffset.UtcNow));
        command.Parameters.AddWithValue("$message", appliedToMessageId);
        command.Parameters.AddWithValue("$id", interventionId);
        await command.ExecuteNonQueryAsync(ct);
    }

    private async Task InitializeAsync()
    {
        await using var connection = await OpenAsync(CancellationToken.None);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA journal_mode = WAL;
            CREATE TABLE IF NOT EXISTS interventions (
                intervention_id TEXT NOT NULL PRIMARY KEY,
                mission_id TEXT NOT NULL,
                message_id TEXT NOT NULL,
                target_instance_id TEXT NULL,
                body TEXT NOT NULL,
                created_at TEXT NOT NULL,
                applied_at TEXT NULL,
                applied_to_message_id TEXT NULL
            );
            CREATE INDEX IF NOT EXISTS ix_interventions_pending
                ON interventions(mission_id, applied_at, created_at);
            """;
        await command.ExecuteNonQueryAsync();
    }

    private async Task EnsureInitializedAsync(CancellationToken ct) => await _initialization.WaitAsync(ct);

    private async Task<SqliteConnection> OpenAsync(CancellationToken ct)
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

    private static Intervention Read(SqliteDataReader reader) => new()
    {
        InterventionId = reader.GetString(0),
        MissionId = reader.GetString(1),
        MessageId = reader.GetString(2),
        TargetInstanceId = reader.IsDBNull(3) ? null : reader.GetString(3),
        Body = reader.GetString(4),
        CreatedAt = Parse(reader.GetString(5)),
        AppliedAt = reader.IsDBNull(6) ? null : Parse(reader.GetString(6)),
        AppliedToMessageId = reader.IsDBNull(7) ? null : reader.GetString(7),
    };

    private static string Format(DateTimeOffset value) => value.ToString("O", CultureInfo.InvariantCulture);

    private static string? Format(DateTimeOffset? value) => value?.ToString("O", CultureInfo.InvariantCulture);

    private static DateTimeOffset Parse(string value)
        => DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
}
