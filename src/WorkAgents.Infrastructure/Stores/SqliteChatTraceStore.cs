using System.Globalization;
using Microsoft.Data.Sqlite;
using WorkAgents.Core;
using WorkAgents.Core.Abstractions;

namespace WorkAgents.Infrastructure.Stores;

/// <summary>Localプロファイル用のSQLite チャットトレースストア。</summary>
public sealed class SqliteChatTraceStore : IChatTraceStore
{
    private readonly string _connectionString;
    private readonly Task _initialization;

    public SqliteChatTraceStore(string databasePath)
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

    public async Task AppendAsync(ChatTraceEntry entry, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        await EnsureInitializedAsync(ct);

        await using var connection = await OpenConnectionAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO chat_trace_entries (
                thread_id, agent_name, model_name, provider, duration_ms, success, error_message, created_at)
            VALUES (
                $thread_id, $agent_name, $model_name, $provider, $duration_ms, $success, $error_message, $created_at);
            """;
        command.Parameters.AddWithValue("$thread_id", entry.ThreadId);
        command.Parameters.AddWithValue("$agent_name", entry.AgentName);
        command.Parameters.AddWithValue("$model_name", (object?)entry.ModelName ?? DBNull.Value);
        command.Parameters.AddWithValue("$provider", (object?)entry.Provider ?? DBNull.Value);
        command.Parameters.AddWithValue("$duration_ms", entry.DurationMs);
        command.Parameters.AddWithValue("$success", entry.Success);
        command.Parameters.AddWithValue("$error_message", (object?)entry.ErrorMessage ?? DBNull.Value);
        command.Parameters.AddWithValue("$created_at", entry.CreatedAt.ToString("O", CultureInfo.InvariantCulture));
        await command.ExecuteNonQueryAsync(ct);
    }

    public async Task<IReadOnlyList<ChatTraceEntry>> ListAsync(string threadId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(threadId);
        await EnsureInitializedAsync(ct);

        await using var connection = await OpenConnectionAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT thread_id, agent_name, model_name, provider, duration_ms, success, error_message, created_at
            FROM chat_trace_entries
            WHERE thread_id = $thread_id
            ORDER BY id;
            """;
        command.Parameters.AddWithValue("$thread_id", threadId);

        var entries = new List<ChatTraceEntry>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            entries.Add(Read(reader));
        }
        return entries;
    }

    private async Task InitializeAsync()
    {
        await using var connection = await OpenConnectionAsync(CancellationToken.None);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA journal_mode = WAL;
            CREATE TABLE IF NOT EXISTS chat_trace_entries (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                thread_id TEXT NOT NULL,
                agent_name TEXT NOT NULL,
                model_name TEXT NULL,
                provider TEXT NULL,
                duration_ms INTEGER NOT NULL,
                success INTEGER NOT NULL,
                error_message TEXT NULL,
                created_at TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS ix_chat_trace_thread
                ON chat_trace_entries(thread_id, id);
            """;
        await command.ExecuteNonQueryAsync();
    }

    private async Task EnsureInitializedAsync(CancellationToken ct) => await _initialization.WaitAsync(ct);

    private async Task<SqliteConnection> OpenConnectionAsync(CancellationToken ct)
    {
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(ct);
        return connection;
    }

    private static ChatTraceEntry Read(SqliteDataReader reader) => new()
    {
        ThreadId = reader.GetString(0),
        AgentName = reader.GetString(1),
        ModelName = reader.IsDBNull(2) ? null : reader.GetString(2),
        Provider = reader.IsDBNull(3) ? null : reader.GetString(3),
        DurationMs = reader.GetInt64(4),
        Success = reader.GetBoolean(5),
        ErrorMessage = reader.IsDBNull(6) ? null : reader.GetString(6),
        CreatedAt = DateTimeOffset.Parse(reader.GetString(7), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
    };
}
