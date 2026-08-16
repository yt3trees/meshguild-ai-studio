using System.Globalization;
using Microsoft.Data.Sqlite;
using WorkAgents.Core;
using WorkAgents.Core.Abstractions;

namespace WorkAgents.Infrastructure.Stores;

/// <summary>Localプロファイル用のSQLiteセッションストア。</summary>
public sealed class SqliteSessionStore : ISessionStore
{
    private readonly string _connectionString;
    private readonly Task _initialization;

    public SqliteSessionStore(string databasePath)
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
        _initialization = InitializeAsync();
    }

    public async Task SaveAsync(SessionRecord session, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(session.ThreadId);
        ArgumentException.ThrowIfNullOrWhiteSpace(session.AgentName);
        ArgumentException.ThrowIfNullOrWhiteSpace(session.SerializedState);
        await EnsureInitializedAsync(ct);

        var now = DateTimeOffset.UtcNow;
        var createdAt = session.CreatedAt == default ? now : session.CreatedAt;
        await using var connection = await OpenConnectionAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO sessions (
                thread_id, agent_name, serialized_state, created_at, updated_at)
            VALUES ($thread_id, $agent_name, $serialized_state, $created_at, $updated_at)
            ON CONFLICT(thread_id) DO UPDATE SET
                serialized_state = excluded.serialized_state,
                updated_at = excluded.updated_at
            WHERE sessions.agent_name = excluded.agent_name;
            """;
        command.Parameters.AddWithValue("$thread_id", session.ThreadId);
        command.Parameters.AddWithValue("$agent_name", session.AgentName);
        command.Parameters.AddWithValue("$serialized_state", session.SerializedState);
        command.Parameters.AddWithValue("$created_at", FormatDate(createdAt));
        command.Parameters.AddWithValue("$updated_at", FormatDate(now));

        if (await command.ExecuteNonQueryAsync(ct) == 1)
        {
            return;
        }

        var existingAgent = await ReadAgentNameAsync(connection, session.ThreadId, ct);
        if (existingAgent is not null && !string.Equals(existingAgent, session.AgentName, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Thread '{session.ThreadId}' already belongs to agent '{existingAgent}'.");
        }

        throw new InvalidOperationException($"Session save failed for thread '{session.ThreadId}'.");
    }

    public async Task<SessionRecord?> LoadAsync(string threadId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(threadId);
        await EnsureInitializedAsync(ct);

        await using var connection = await OpenConnectionAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT thread_id, agent_name, serialized_state, created_at, updated_at
            FROM sessions
            WHERE thread_id = $thread_id;
            """;
        command.Parameters.AddWithValue("$thread_id", threadId);

        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
        {
            return null;
        }

        return new SessionRecord
        {
            ThreadId = reader.GetString(0),
            AgentName = reader.GetString(1),
            SerializedState = reader.GetString(2),
            CreatedAt = ParseDate(reader.GetString(3)),
            UpdatedAt = ParseDate(reader.GetString(4)),
        };
    }

    private async Task InitializeAsync()
    {
        await using var connection = await OpenConnectionAsync(CancellationToken.None);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA journal_mode = WAL;
            CREATE TABLE IF NOT EXISTS sessions (
                thread_id TEXT NOT NULL PRIMARY KEY,
                agent_name TEXT NOT NULL,
                serialized_state TEXT NOT NULL,
                created_at TEXT NOT NULL,
                updated_at TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS ix_sessions_agent_updated_at
                ON sessions(agent_name, updated_at DESC);
            """;
        await command.ExecuteNonQueryAsync();
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

    private static async Task<string?> ReadAgentNameAsync(
        SqliteConnection connection,
        string threadId,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT agent_name FROM sessions WHERE thread_id = $thread_id;";
        command.Parameters.AddWithValue("$thread_id", threadId);
        var value = await command.ExecuteScalarAsync(ct);
        return value is null or DBNull ? null : Convert.ToString(value, CultureInfo.InvariantCulture);
    }

    private static string FormatDate(DateTimeOffset value)
        => value.ToString("O", CultureInfo.InvariantCulture);

    private static DateTimeOffset ParseDate(string value)
        => DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
}