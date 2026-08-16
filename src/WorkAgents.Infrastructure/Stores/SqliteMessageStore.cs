using System.Globalization;
using Microsoft.Data.Sqlite;
using WorkAgents.Core.Abstractions;
using WorkAgents.Core.Missions;

namespace WorkAgents.Infrastructure.Stores;

/// <summary>Localプロファイル用のSQLiteメッセージストア (messages / conversation_summaries テーブル、T028)。</summary>
public sealed class SqliteMessageStore : IMessageStore
{
    private readonly string _connectionString;
    private readonly ISecretRedactor? _redactor;
    private readonly Task _initialization;

    public SqliteMessageStore(string databasePath, ISecretRedactor? redactor = null)
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
        _redactor = redactor;
        _initialization = InitializeAsync();
    }

    public async Task<Message> AppendAsync(Message message, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        await EnsureInitializedAsync(ct);

        await using var connection = await OpenConnectionAsync(ct);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(ct);

        await using var seqCommand = connection.CreateCommand();
        seqCommand.Transaction = transaction;
        seqCommand.CommandText = "SELECT COALESCE(MAX(seq), 0) + 1 FROM messages WHERE mission_id = $mission_id;";
        seqCommand.Parameters.AddWithValue("$mission_id", message.MissionId);
        var seq = Convert.ToInt64(await seqCommand.ExecuteScalarAsync(ct), CultureInfo.InvariantCulture);

        var toInsert = message with
        {
            Seq = seq,
            Body = _redactor is null ? message.Body : await _redactor.RedactAsync(message.Body, ct),
            InputRefs = _redactor is null || message.InputRefs is null ? message.InputRefs : await _redactor.RedactAsync(message.InputRefs, ct),
        };

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO messages (
                message_id, mission_id, seq, thread_key, sender_kind, sender_instance_id,
                recipient_instance_id, kind, body, in_reply_to, delegation_depth,
                node_run_id, iteration_id, input_refs, cost_record_id,
                discarded_at, discarded_by_checkpoint_id, created_at)
            VALUES (
                $message_id, $mission_id, $seq, $thread_key, $sender_kind, $sender_instance_id,
                $recipient_instance_id, $kind, $body, $in_reply_to, $delegation_depth,
                $node_run_id, $iteration_id, $input_refs, $cost_record_id,
                $discarded_at, $discarded_by_checkpoint_id, $created_at);
            """;
        AddMessageParameters(command, toInsert);
        await command.ExecuteNonQueryAsync(ct);
        await transaction.CommitAsync(ct);
        return toInsert;
    }

    public async Task<Message?> GetAsync(string messageId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(messageId);
        await EnsureInitializedAsync(ct);

        await using var connection = await OpenConnectionAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = SelectSql + " WHERE message_id = $message_id;";
        command.Parameters.AddWithValue("$message_id", messageId);

        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? ReadMessage(reader) : null;
    }

    public async Task<IReadOnlyList<Message>> ListAsync(
        string missionId,
        long sinceSeq = 0,
        string? threadKey = null,
        bool includeDiscarded = false,
        int limit = 500,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(missionId);
        await EnsureInitializedAsync(ct);

        await using var connection = await OpenConnectionAsync(ct);
        await using var command = connection.CreateCommand();
        var sql = SelectSql + " WHERE mission_id = $mission_id AND seq > $since_seq";
        command.Parameters.AddWithValue("$mission_id", missionId);
        command.Parameters.AddWithValue("$since_seq", sinceSeq);

        if (!string.IsNullOrWhiteSpace(threadKey))
        {
            sql += " AND thread_key = $thread_key";
            command.Parameters.AddWithValue("$thread_key", threadKey);
        }

        if (!includeDiscarded)
        {
            sql += " AND discarded_at IS NULL";
        }

        sql += " ORDER BY seq ASC LIMIT $limit;";
        command.Parameters.AddWithValue("$limit", limit);
        command.CommandText = sql;

        var messages = new List<Message>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            messages.Add(ReadMessage(reader));
        }
        return messages;
    }

    public async Task DiscardAsync(string missionId, long afterSeq, string checkpointId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(missionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(checkpointId);
        await EnsureInitializedAsync(ct);

        await using var connection = await OpenConnectionAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE messages
            SET discarded_at = $now, discarded_by_checkpoint_id = $checkpoint_id
            WHERE mission_id = $mission_id AND seq > $after_seq AND discarded_at IS NULL;
            """;
        command.Parameters.AddWithValue("$now", FormatDate(DateTimeOffset.UtcNow));
        command.Parameters.AddWithValue("$checkpoint_id", checkpointId);
        command.Parameters.AddWithValue("$mission_id", missionId);
        command.Parameters.AddWithValue("$after_seq", afterSeq);
        await command.ExecuteNonQueryAsync(ct);
    }

    public async Task AddSummaryAsync(ConversationSummary summary, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(summary);
        await EnsureInitializedAsync(ct);

        await using var connection = await OpenConnectionAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO conversation_summaries (
                summary_id, mission_id, thread_key, covers_up_to_seq, body, boundary_kind, created_at)
            VALUES ($summary_id, $mission_id, $thread_key, $covers_up_to_seq, $body, $boundary_kind, $created_at);
            """;
        command.Parameters.AddWithValue("$summary_id", summary.SummaryId);
        command.Parameters.AddWithValue("$mission_id", summary.MissionId);
        command.Parameters.AddWithValue("$thread_key", summary.ThreadKey);
        command.Parameters.AddWithValue("$covers_up_to_seq", summary.CoversUpToSeq);
        command.Parameters.AddWithValue("$body", _redactor is null ? summary.Body : await _redactor.RedactAsync(summary.Body, ct));
        command.Parameters.AddWithValue("$boundary_kind", summary.BoundaryKind.ToString());
        command.Parameters.AddWithValue("$created_at", FormatDate(summary.CreatedAt));
        await command.ExecuteNonQueryAsync(ct);
    }

    public async Task<IReadOnlyList<ConversationSummary>> ListSummariesAsync(
        string missionId,
        string? threadKey = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(missionId);
        await EnsureInitializedAsync(ct);

        await using var connection = await OpenConnectionAsync(ct);
        await using var command = connection.CreateCommand();
        var sql = """
            SELECT summary_id, mission_id, thread_key, covers_up_to_seq, body, boundary_kind, created_at
            FROM conversation_summaries WHERE mission_id = $mission_id
            """;
        command.Parameters.AddWithValue("$mission_id", missionId);
        if (!string.IsNullOrWhiteSpace(threadKey))
        {
            sql += " AND thread_key = $thread_key";
            command.Parameters.AddWithValue("$thread_key", threadKey);
        }
        sql += " ORDER BY covers_up_to_seq ASC;";
        command.CommandText = sql;

        var summaries = new List<ConversationSummary>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            summaries.Add(new ConversationSummary
            {
                SummaryId = reader.GetString(0),
                MissionId = reader.GetString(1),
                ThreadKey = reader.GetString(2),
                CoversUpToSeq = reader.GetInt64(3),
                Body = reader.GetString(4),
                BoundaryKind = Enum.Parse<SummaryBoundaryKind>(reader.GetString(5)),
                CreatedAt = ParseDate(reader.GetString(6)),
            });
        }
        return summaries;
    }

    private const string SelectSql = """
        SELECT message_id, mission_id, seq, thread_key, sender_kind, sender_instance_id,
               recipient_instance_id, kind, body, in_reply_to, delegation_depth,
               node_run_id, iteration_id, input_refs, cost_record_id,
               discarded_at, discarded_by_checkpoint_id, created_at
        FROM messages
        """;

    private async Task InitializeAsync()
    {
        await using var connection = await OpenConnectionAsync(CancellationToken.None);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA journal_mode = WAL;
            CREATE TABLE IF NOT EXISTS messages (
                message_id TEXT NOT NULL PRIMARY KEY,
                mission_id TEXT NOT NULL,
                seq INTEGER NOT NULL,
                thread_key TEXT NOT NULL DEFAULT 'main',
                sender_kind TEXT NOT NULL,
                sender_instance_id TEXT NULL,
                recipient_instance_id TEXT NULL,
                kind TEXT NOT NULL,
                body TEXT NOT NULL,
                in_reply_to TEXT NULL,
                delegation_depth INTEGER NOT NULL DEFAULT 0,
                node_run_id TEXT NULL,
                iteration_id TEXT NULL,
                input_refs TEXT NULL,
                cost_record_id TEXT NULL,
                discarded_at TEXT NULL,
                discarded_by_checkpoint_id TEXT NULL,
                created_at TEXT NOT NULL
            );
            CREATE UNIQUE INDEX IF NOT EXISTS ux_messages_mission_seq ON messages(mission_id, seq);
            CREATE INDEX IF NOT EXISTS ix_messages_mission_thread ON messages(mission_id, thread_key, seq);

            CREATE TABLE IF NOT EXISTS conversation_summaries (
                summary_id TEXT NOT NULL PRIMARY KEY,
                mission_id TEXT NOT NULL,
                thread_key TEXT NOT NULL DEFAULT 'main',
                covers_up_to_seq INTEGER NOT NULL,
                body TEXT NOT NULL,
                boundary_kind TEXT NOT NULL,
                created_at TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS ix_conversation_summaries_mission ON conversation_summaries(mission_id, thread_key);
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

    private static void AddMessageParameters(SqliteCommand command, Message message)
    {
        command.Parameters.AddWithValue("$message_id", message.MessageId);
        command.Parameters.AddWithValue("$mission_id", message.MissionId);
        command.Parameters.AddWithValue("$seq", message.Seq);
        command.Parameters.AddWithValue("$thread_key", message.ThreadKey);
        command.Parameters.AddWithValue("$sender_kind", message.SenderKind.ToString());
        command.Parameters.AddWithValue("$sender_instance_id", (object?)message.SenderInstanceId ?? DBNull.Value);
        command.Parameters.AddWithValue("$recipient_instance_id", (object?)message.RecipientInstanceId ?? DBNull.Value);
        command.Parameters.AddWithValue("$kind", message.Kind.ToString());
        command.Parameters.AddWithValue("$body", message.Body);
        command.Parameters.AddWithValue("$in_reply_to", (object?)message.InReplyTo ?? DBNull.Value);
        command.Parameters.AddWithValue("$delegation_depth", message.DelegationDepth);
        command.Parameters.AddWithValue("$node_run_id", (object?)message.NodeRunId ?? DBNull.Value);
        command.Parameters.AddWithValue("$iteration_id", (object?)message.IterationId ?? DBNull.Value);
        command.Parameters.AddWithValue("$input_refs", (object?)message.InputRefs ?? DBNull.Value);
        command.Parameters.AddWithValue("$cost_record_id", (object?)message.CostRecordId ?? DBNull.Value);
        command.Parameters.AddWithValue("$discarded_at", (object?)FormatDate(message.DiscardedAt) ?? DBNull.Value);
        command.Parameters.AddWithValue("$discarded_by_checkpoint_id", (object?)message.DiscardedByCheckpointId ?? DBNull.Value);
        command.Parameters.AddWithValue("$created_at", FormatDate(message.CreatedAt));
    }

    private static Message ReadMessage(SqliteDataReader reader)
    {
        return new Message
        {
            MessageId = reader.GetString(0),
            MissionId = reader.GetString(1),
            Seq = reader.GetInt64(2),
            ThreadKey = reader.GetString(3),
            SenderKind = Enum.Parse<MessageSenderKind>(reader.GetString(4)),
            SenderInstanceId = ReadNullableString(reader, 5),
            RecipientInstanceId = ReadNullableString(reader, 6),
            Kind = Enum.Parse<MessageKind>(reader.GetString(7)),
            Body = reader.GetString(8),
            InReplyTo = ReadNullableString(reader, 9),
            DelegationDepth = reader.GetInt32(10),
            NodeRunId = ReadNullableString(reader, 11),
            IterationId = ReadNullableString(reader, 12),
            InputRefs = ReadNullableString(reader, 13),
            CostRecordId = ReadNullableString(reader, 14),
            DiscardedAt = ReadNullableString(reader, 15) is { } d ? ParseDate(d) : null,
            DiscardedByCheckpointId = ReadNullableString(reader, 16),
            CreatedAt = ParseDate(reader.GetString(17)),
        };
    }

    private static string? ReadNullableString(SqliteDataReader reader, int ordinal)
        => reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);

    private static string FormatDate(DateTimeOffset value)
        => value.ToString("O", CultureInfo.InvariantCulture);

    private static string? FormatDate(DateTimeOffset? value)
        => value?.ToString("O", CultureInfo.InvariantCulture);

    private static DateTimeOffset ParseDate(string value)
        => DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
}
