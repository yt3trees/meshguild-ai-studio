using System.Globalization;
using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using WorkAgents.Core.Abstractions;
using WorkAgents.Core.Missions;

namespace WorkAgents.Infrastructure.Stores;

/// <summary>Local artifact files plus redacted mission metadata in SQLite.</summary>
public sealed class SqliteArtifactStore : IMissionArtifactStore
{
    private readonly string _connectionString;
    private readonly string _root;
    private readonly ISecretRedactor? _redactor;
    private readonly Task _initialization;

    public SqliteArtifactStore(string databasePath, string artifactsRoot, ISecretRedactor? redactor = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactsRoot);
        var fullDatabasePath = Path.GetFullPath(databasePath);
        var databaseDirectory = Path.GetDirectoryName(fullDatabasePath);
        if (!string.IsNullOrEmpty(databaseDirectory)) Directory.CreateDirectory(databaseDirectory);
        _root = Path.GetFullPath(artifactsRoot);
        Directory.CreateDirectory(_root);
        _redactor = redactor;
        _connectionString = new SqliteConnectionStringBuilder { DataSource = fullDatabasePath, Mode = SqliteOpenMode.ReadWriteCreate, Cache = SqliteCacheMode.Shared, Pooling = false }.ToString();
        _initialization = InitializeAsync();
    }

    public async Task<string> SaveAsync(string purpose, string fileName, Stream content, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(purpose);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentNullException.ThrowIfNull(content);
        var safePurpose = Path.GetFileName(purpose);
        var safeFileName = Path.GetFileName(fileName);
        var directory = Path.Combine(_root, safePurpose);
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, safeFileName);
        await using var output = File.Create(path);
        await content.CopyToAsync(output, ct);
        return path;
    }

    public Task<Stream?> OpenReadAsync(string uri, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(uri)) return Task.FromResult<Stream?>(null);
        var fullPath = Path.GetFullPath(uri);
        if (!fullPath.StartsWith(_root, StringComparison.OrdinalIgnoreCase) || !File.Exists(fullPath)) return Task.FromResult<Stream?>(null);
        return Task.FromResult<Stream?>(File.OpenRead(fullPath));
    }

    public async Task SaveMissionArtifactAsync(MissionArtifact artifact, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(artifact);
        await EnsureInitializedAsync(ct);
        var summary = _redactor is null ? artifact.Summary : await _redactor.RedactAsync(artifact.Summary, ct);
        await using var connection = await OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO artifacts (
                artifact_id, mission_id, source_message_id, iteration_id, node_run_id, path,
                summary, content_hash, discarded_at, created_at)
            VALUES ($id, $mission, $message, $iteration, $node, $path, $summary, $hash, $discarded, $created);
            """;
        command.Parameters.AddWithValue("$id", artifact.ArtifactId);
        command.Parameters.AddWithValue("$mission", artifact.MissionId);
        command.Parameters.AddWithValue("$message", artifact.SourceMessageId);
        command.Parameters.AddWithValue("$iteration", (object?)artifact.IterationId ?? DBNull.Value);
        command.Parameters.AddWithValue("$node", (object?)artifact.NodeRunId ?? DBNull.Value);
        command.Parameters.AddWithValue("$path", artifact.Path);
        command.Parameters.AddWithValue("$summary", summary);
        command.Parameters.AddWithValue("$hash", artifact.ContentHash);
        command.Parameters.AddWithValue("$discarded", (object?)Format(artifact.DiscardedAt) ?? DBNull.Value);
        command.Parameters.AddWithValue("$created", Format(artifact.CreatedAt));
        await command.ExecuteNonQueryAsync(ct);
    }

    public async Task<IReadOnlyList<MissionArtifact>> ListMissionAsync(string missionId, bool includeDiscarded = false, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        await using var connection = await OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT artifact_id, mission_id, source_message_id, iteration_id, node_run_id, path,
                   summary, content_hash, discarded_at, created_at
            FROM artifacts WHERE mission_id = $mission
            """ + (includeDiscarded ? "" : " AND discarded_at IS NULL") + " ORDER BY created_at ASC;";
        command.Parameters.AddWithValue("$mission", missionId);
        var list = new List<MissionArtifact>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            list.Add(new MissionArtifact
            {
                ArtifactId = reader.GetString(0),
                MissionId = reader.GetString(1),
                SourceMessageId = reader.GetString(2),
                IterationId = reader.IsDBNull(3) ? null : reader.GetString(3),
                NodeRunId = reader.IsDBNull(4) ? null : reader.GetString(4),
                Path = reader.GetString(5),
                Summary = reader.GetString(6),
                ContentHash = reader.GetString(7),
                DiscardedAt = reader.IsDBNull(8) ? null : Parse(reader.GetString(8)),
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
            CREATE TABLE IF NOT EXISTS artifacts (
                artifact_id TEXT NOT NULL PRIMARY KEY,
                mission_id TEXT NOT NULL,
                source_message_id TEXT NOT NULL,
                iteration_id TEXT NULL,
                node_run_id TEXT NULL,
                path TEXT NOT NULL,
                summary TEXT NOT NULL,
                content_hash TEXT NOT NULL,
                discarded_at TEXT NULL,
                created_at TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS ix_artifacts_mission ON artifacts(mission_id, created_at);
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
    private static string? Format(DateTimeOffset? value) => value?.ToString("O", CultureInfo.InvariantCulture);
    private static DateTimeOffset Parse(string value) => DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
}
