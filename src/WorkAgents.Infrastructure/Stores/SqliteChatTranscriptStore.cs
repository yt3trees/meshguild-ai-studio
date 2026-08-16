using System.Globalization;
using Microsoft.Data.Sqlite;
using WorkAgents.Core;
using WorkAgents.Core.Abstractions;

namespace WorkAgents.Infrastructure.Stores;

/// <summary>Localプロファイル用のSQLite チャット会話ログストア。</summary>
public sealed class SqliteChatTranscriptStore : IChatTranscriptStore
{
    private readonly string _connectionString;
    private readonly Task _initialization;

    public SqliteChatTranscriptStore(string databasePath)
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

    public async Task AppendAsync(ChatTranscriptEntry entry, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        await EnsureInitializedAsync(ct);

        await using var connection = await OpenConnectionAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO chat_transcript_entries (thread_id, agent_name, role, content, is_error, created_at)
            VALUES ($thread_id, $agent_name, $role, $content, $is_error, $created_at);
            """;
        command.Parameters.AddWithValue("$thread_id", entry.ThreadId);
        command.Parameters.AddWithValue("$agent_name", entry.AgentName);
        command.Parameters.AddWithValue("$role", entry.Role);
        command.Parameters.AddWithValue("$content", entry.Content);
        command.Parameters.AddWithValue("$is_error", entry.IsError);
        command.Parameters.AddWithValue("$created_at", entry.CreatedAt.ToString("O", CultureInfo.InvariantCulture));
        await command.ExecuteNonQueryAsync(ct);
    }

    public async Task<IReadOnlyList<ChatTranscriptEntry>> ListAsync(string threadId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(threadId);
        await EnsureInitializedAsync(ct);

        await using var connection = await OpenConnectionAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT thread_id, agent_name, role, content, is_error, created_at
            FROM chat_transcript_entries
            WHERE thread_id = $thread_id
            ORDER BY id;
            """;
        command.Parameters.AddWithValue("$thread_id", threadId);

        var entries = new List<ChatTranscriptEntry>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            entries.Add(Read(reader));
        }
        return entries;
    }

    public async Task<string?> GetLatestThreadIdAsync(string agentName, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentName);
        await EnsureInitializedAsync(ct);

        await using var connection = await OpenConnectionAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT thread_id FROM chat_transcript_entries
            WHERE agent_name = $agent_name
            ORDER BY id DESC
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$agent_name", agentName);
        return await command.ExecuteScalarAsync(ct) as string;
    }

    private async Task InitializeAsync()
    {
        await using var connection = await OpenConnectionAsync(CancellationToken.None);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA journal_mode = WAL;
            CREATE TABLE IF NOT EXISTS chat_transcript_entries (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                thread_id TEXT NOT NULL,
                agent_name TEXT NOT NULL,
                role TEXT NOT NULL,
                content TEXT NOT NULL,
                is_error INTEGER NOT NULL,
                created_at TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS ix_chat_transcript_thread
                ON chat_transcript_entries(thread_id, id);
            CREATE INDEX IF NOT EXISTS ix_chat_transcript_agent
                ON chat_transcript_entries(agent_name, id DESC);
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

    private static ChatTranscriptEntry Read(SqliteDataReader reader) => new()
    {
        ThreadId = reader.GetString(0),
        AgentName = reader.GetString(1),
        Role = reader.GetString(2),
        Content = reader.GetString(3),
        IsError = reader.GetBoolean(4),
        CreatedAt = DateTimeOffset.Parse(reader.GetString(5), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
    };
}
