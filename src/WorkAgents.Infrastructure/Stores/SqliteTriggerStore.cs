using System.Globalization;
using Microsoft.Data.Sqlite;
using WorkAgents.Core.Abstractions;
using WorkAgents.Core.Triggers;

namespace WorkAgents.Infrastructure.Stores;

/// <summary>SQLite trigger and fire history store.</summary>
public sealed class SqliteTriggerStore : ITriggerStore
{
    private readonly string _connectionString;
    private readonly Task _initialization;

    public SqliteTriggerStore(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        var fullPath = Path.GetFullPath(databasePath);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = fullPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            Pooling = false,
        }.ToString();
        _initialization = InitializeAsync();
    }

    public async Task CreateAsync(TriggerDefinition trigger, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(trigger);
        Validate(trigger);
        await EnsureInitializedAsync(ct);
        await using var connection = await OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = UpsertSql;
        AddTriggerParameters(command, trigger);
        await command.ExecuteNonQueryAsync(ct);
    }

    public async Task<TriggerDefinition?> GetAsync(string name, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        await using var connection = await OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = SelectTriggerSql + " WHERE name = $name;";
        command.Parameters.AddWithValue("$name", name);
        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? ReadTrigger(reader) : null;
    }

    public async Task<IReadOnlyList<TriggerDefinition>> ListAsync(CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        await using var connection = await OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = SelectTriggerSql + " ORDER BY name ASC;";
        var list = new List<TriggerDefinition>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) list.Add(ReadTrigger(reader));
        return list;
    }

    public Task UpdateAsync(TriggerDefinition trigger, CancellationToken ct = default) => CreateAsync(trigger, ct);

    public async Task DeleteAsync(string name, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        await using var connection = await OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM triggers WHERE name = $name;";
        command.Parameters.AddWithValue("$name", name);
        await command.ExecuteNonQueryAsync(ct);
    }

    public async Task SetEnabledAsync(string name, bool enabled, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        await using var connection = await OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE triggers SET enabled = $enabled, updated_at = $updated WHERE name = $name;";
        command.Parameters.AddWithValue("$enabled", enabled ? 1 : 0);
        command.Parameters.AddWithValue("$updated", Format(DateTimeOffset.UtcNow));
        command.Parameters.AddWithValue("$name", name);
        await command.ExecuteNonQueryAsync(ct);
    }

    public async Task RecordFireAsync(TriggerFire fire, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(fire);
        await EnsureInitializedAsync(ct);
        await using var connection = await OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO trigger_fires (fire_id, trigger_id, fired_at, decision, decision_reason, mission_id)
            VALUES ($id, $trigger, $fired, $decision, $reason, $mission);
            """;
        command.Parameters.AddWithValue("$id", fire.FireId);
        command.Parameters.AddWithValue("$trigger", fire.TriggerId);
        command.Parameters.AddWithValue("$fired", Format(fire.FiredAt));
        command.Parameters.AddWithValue("$decision", fire.Decision.ToString());
        command.Parameters.AddWithValue("$reason", fire.DecisionReason);
        command.Parameters.AddWithValue("$mission", (object?)fire.MissionId ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(ct);
    }

    public async Task<IReadOnlyList<TriggerFire>> ListFiresAsync(string triggerId, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        await using var connection = await OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT fire_id, trigger_id, fired_at, decision, decision_reason, mission_id
            FROM trigger_fires WHERE trigger_id = $trigger ORDER BY fired_at DESC;
            """;
        command.Parameters.AddWithValue("$trigger", triggerId);
        var list = new List<TriggerFire>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            list.Add(new TriggerFire
            {
                FireId = reader.GetString(0),
                TriggerId = reader.GetString(1),
                FiredAt = Parse(reader.GetString(2)),
                Decision = Enum.Parse<TriggerDecision>(reader.GetString(3)),
                DecisionReason = reader.GetString(4),
                MissionId = reader.IsDBNull(5) ? null : reader.GetString(5),
            });
        }
        return list;
    }

    private const string SelectTriggerSql = """
        SELECT trigger_id, name, kind, target_kind, target_name, input, cron, interval_seconds,
               overlap_policy, enabled, secret_ref, last_run_at, next_run_at, created_at, updated_at
        FROM triggers
        """;
    private const string UpsertSql = """
        INSERT INTO triggers (
            trigger_id, name, kind, target_kind, target_name, input, cron, interval_seconds,
            overlap_policy, enabled, secret_ref, last_run_at, next_run_at, created_at, updated_at)
        VALUES ($id, $name, $kind, $target_kind, $target_name, $input, $cron, $interval,
                $overlap, $enabled, $secret, $last, $next, $created, $updated)
        ON CONFLICT(name) DO UPDATE SET
            kind = excluded.kind, target_kind = excluded.target_kind, target_name = excluded.target_name,
            input = excluded.input, cron = excluded.cron, interval_seconds = excluded.interval_seconds,
            overlap_policy = excluded.overlap_policy, enabled = excluded.enabled, secret_ref = excluded.secret_ref,
            last_run_at = excluded.last_run_at, next_run_at = excluded.next_run_at, updated_at = excluded.updated_at;
        """;

    private async Task InitializeAsync()
    {
        await using var connection = await OpenAsync(CancellationToken.None);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA journal_mode = WAL;
            CREATE TABLE IF NOT EXISTS triggers (
                trigger_id TEXT NOT NULL PRIMARY KEY,
                name TEXT NOT NULL UNIQUE,
                kind TEXT NOT NULL,
                target_kind TEXT NOT NULL,
                target_name TEXT NOT NULL,
                input TEXT NOT NULL,
                cron TEXT NULL,
                interval_seconds INTEGER NULL,
                overlap_policy TEXT NOT NULL,
                enabled INTEGER NOT NULL,
                secret_ref TEXT NULL,
                last_run_at TEXT NULL,
                next_run_at TEXT NULL,
                created_at TEXT NOT NULL,
                updated_at TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS trigger_fires (
                fire_id TEXT NOT NULL PRIMARY KEY,
                trigger_id TEXT NOT NULL,
                fired_at TEXT NOT NULL,
                decision TEXT NOT NULL,
                decision_reason TEXT NOT NULL,
                mission_id TEXT NULL
            );
            CREATE INDEX IF NOT EXISTS ix_triggers_due ON triggers(enabled, next_run_at);
            """;
        await command.ExecuteNonQueryAsync();
    }

    private static void Validate(TriggerDefinition trigger)
    {
        if (trigger.Kind == TriggerKind.Schedule && string.IsNullOrWhiteSpace(trigger.Cron)) throw new ArgumentException("Schedule triggers require cron.");
        if (trigger.Kind == TriggerKind.Interval && trigger.IntervalSeconds is not > 0) throw new ArgumentException("Interval triggers require a positive interval.");
        if (trigger.Kind == TriggerKind.Event && string.IsNullOrWhiteSpace(trigger.SecretRef)) throw new ArgumentException("Event triggers require a secret reference.");
    }

    private static void AddTriggerParameters(SqliteCommand command, TriggerDefinition trigger)
    {
        command.Parameters.AddWithValue("$id", trigger.TriggerId);
        command.Parameters.AddWithValue("$name", trigger.Name);
        command.Parameters.AddWithValue("$kind", trigger.Kind.ToString());
        command.Parameters.AddWithValue("$target_kind", trigger.TargetKind);
        command.Parameters.AddWithValue("$target_name", trigger.TargetName);
        command.Parameters.AddWithValue("$input", trigger.Input);
        command.Parameters.AddWithValue("$cron", (object?)trigger.Cron ?? DBNull.Value);
        command.Parameters.AddWithValue("$interval", (object?)trigger.IntervalSeconds ?? DBNull.Value);
        command.Parameters.AddWithValue("$overlap", trigger.OverlapPolicy.ToString());
        command.Parameters.AddWithValue("$enabled", trigger.Enabled ? 1 : 0);
        command.Parameters.AddWithValue("$secret", (object?)trigger.SecretRef ?? DBNull.Value);
        command.Parameters.AddWithValue("$last", (object?)Format(trigger.LastRunAt) ?? DBNull.Value);
        command.Parameters.AddWithValue("$next", (object?)Format(trigger.NextRunAt) ?? DBNull.Value);
        command.Parameters.AddWithValue("$created", Format(trigger.CreatedAt));
        command.Parameters.AddWithValue("$updated", Format(trigger.UpdatedAt));
    }

    private static TriggerDefinition ReadTrigger(SqliteDataReader reader) => new()
    {
        TriggerId = reader.GetString(0),
        Name = reader.GetString(1),
        Kind = Enum.Parse<TriggerKind>(reader.GetString(2)),
        TargetKind = reader.GetString(3),
        TargetName = reader.GetString(4),
        Input = reader.GetString(5),
        Cron = reader.IsDBNull(6) ? null : reader.GetString(6),
        IntervalSeconds = reader.IsDBNull(7) ? null : reader.GetInt32(7),
        OverlapPolicy = Enum.Parse<OverlapPolicy>(reader.GetString(8)),
        Enabled = reader.GetInt32(9) != 0,
        SecretRef = reader.IsDBNull(10) ? null : reader.GetString(10),
        LastRunAt = reader.IsDBNull(11) ? null : Parse(reader.GetString(11)),
        NextRunAt = reader.IsDBNull(12) ? null : Parse(reader.GetString(12)),
        CreatedAt = Parse(reader.GetString(13)),
        UpdatedAt = Parse(reader.GetString(14)),
    };

    private async Task EnsureInitializedAsync(CancellationToken ct) => await _initialization.WaitAsync(ct);
    private async Task<SqliteConnection> OpenAsync(CancellationToken ct)
    {
        var connection = new SqliteConnection(_connectionString);
        try { await connection.OpenAsync(ct); return connection; }
        catch { await connection.DisposeAsync(); throw; }
    }
    private static string Format(DateTimeOffset value) => value.ToString("O", CultureInfo.InvariantCulture);
    private static string? Format(DateTimeOffset? value) => value?.ToString("O", CultureInfo.InvariantCulture);
    private static DateTimeOffset Parse(string value) => DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
}
