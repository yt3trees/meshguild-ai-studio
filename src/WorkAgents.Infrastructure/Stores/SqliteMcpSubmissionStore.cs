using System.Globalization;
using Microsoft.Data.Sqlite;
using WorkAgents.Core.Abstractions;
using WorkAgents.Core.Missions;

namespace WorkAgents.Infrastructure.Stores;

public sealed class SqliteMcpSubmissionStore : IMcpSubmissionStore
{
    private readonly string _connectionString;
    private readonly Task _initialization;

    public SqliteMcpSubmissionStore(string databasePath)
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

    public async Task<McpSubmission?> GetAsync(string requestKey, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestKey);
        await EnsureInitializedAsync(ct);

        await using var connection = await OpenConnectionAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT request_key, request_hash, mission_id, created_at
            FROM mcp_submissions
            WHERE request_key = $request_key;
            """;
        command.Parameters.AddWithValue("$request_key", requestKey);

        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? ReadSubmission(reader) : null;
    }

    public async Task<bool> TryCreateAsync(McpSubmission submission, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(submission);
        await EnsureInitializedAsync(ct);

        await using var connection = await OpenConnectionAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT OR IGNORE INTO mcp_submissions (request_key, request_hash, mission_id, created_at)
            VALUES ($request_key, $request_hash, $mission_id, $created_at);
            """;
        command.Parameters.AddWithValue("$request_key", submission.RequestKey);
        command.Parameters.AddWithValue("$request_hash", submission.RequestHash);
        command.Parameters.AddWithValue("$mission_id", submission.MissionId);
        command.Parameters.AddWithValue("$created_at", Format(submission.CreatedAt));
        return await command.ExecuteNonQueryAsync(ct) == 1;
    }

    public async Task DeleteExpiredAsync(DateTimeOffset before, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        await using var connection = await OpenConnectionAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM mcp_submissions WHERE created_at < $before;";
        command.Parameters.AddWithValue("$before", Format(before));
        await command.ExecuteNonQueryAsync(ct);
    }

    private async Task InitializeAsync()
    {
        await using var connection = await OpenConnectionAsync(CancellationToken.None);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA journal_mode = WAL;
            CREATE TABLE IF NOT EXISTS mcp_submissions (
                request_key TEXT NOT NULL PRIMARY KEY,
                request_hash TEXT NOT NULL,
                mission_id TEXT NOT NULL UNIQUE,
                created_at TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS ix_mcp_submissions_created_at ON mcp_submissions(created_at);
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

    private static McpSubmission ReadSubmission(SqliteDataReader reader)
        => new()
        {
            RequestKey = reader.GetString(0),
            RequestHash = reader.GetString(1),
            MissionId = reader.GetString(2),
            CreatedAt = DateTimeOffset.Parse(reader.GetString(3), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
        };

    private static string Format(DateTimeOffset value) => value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
}
