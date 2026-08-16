using System.Globalization;
using Microsoft.Data.Sqlite;
using WorkAgents.Core.Abstractions;
using WorkAgents.Core.Graphs;

namespace WorkAgents.Infrastructure.Stores;

/// <summary>SQLite persistence for graph snapshots and graph execution state.</summary>
public sealed class SqliteGraphVersionStore : IGraphVersionStore
{
    private readonly string _connectionString;
    private readonly Task _initialization;

    public SqliteGraphVersionStore(string databasePath)
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

    public async Task<GraphVersion> GetOrCreateVersionAsync(string graphName, string contentHash, Func<int, GraphVersion> factory, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(graphName);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentHash);
        ArgumentNullException.ThrowIfNull(factory);
        await EnsureInitializedAsync(ct);
        await using var connection = await OpenAsync(ct);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(ct);
        await using var existing = connection.CreateCommand();
        existing.Transaction = transaction;
        existing.CommandText = SelectVersionSql + " WHERE graph_name = $name AND content_hash = $hash;";
        existing.Parameters.AddWithValue("$name", graphName);
        existing.Parameters.AddWithValue("$hash", contentHash);
        await using (var reader = await existing.ExecuteReaderAsync(ct))
        {
            if (await reader.ReadAsync(ct))
            {
                var version = ReadVersion(reader);
                await transaction.CommitAsync(ct);
                return version;
            }
        }

        await using var max = connection.CreateCommand();
        max.Transaction = transaction;
        max.CommandText = "SELECT COALESCE(MAX(version_no), 0) + 1 FROM graph_versions WHERE graph_name = $name;";
        max.Parameters.AddWithValue("$name", graphName);
        var versionNo = Convert.ToInt32(await max.ExecuteScalarAsync(ct), CultureInfo.InvariantCulture);
        var created = factory(versionNo);
        await using var insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = """
            INSERT INTO graph_versions (graph_version_id, graph_name, version_no, content_hash, definition_yaml, created_at)
            VALUES ($id, $name, $version, $hash, $yaml, $created);
            """;
        insert.Parameters.AddWithValue("$id", created.GraphVersionId);
        insert.Parameters.AddWithValue("$name", created.GraphName);
        insert.Parameters.AddWithValue("$version", created.VersionNo);
        insert.Parameters.AddWithValue("$hash", created.ContentHash);
        insert.Parameters.AddWithValue("$yaml", created.DefinitionYaml);
        insert.Parameters.AddWithValue("$created", Format(created.CreatedAt));
        await insert.ExecuteNonQueryAsync(ct);
        await transaction.CommitAsync(ct);
        return created;
    }

    public async Task<GraphVersion?> GetVersionAsync(string graphVersionId, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        await using var connection = await OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = SelectVersionSql + " WHERE graph_version_id = $id;";
        command.Parameters.AddWithValue("$id", graphVersionId);
        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? ReadVersion(reader) : null;
    }

    public async Task CreateNodeRunAsync(NodeRun nodeRun, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(nodeRun);
        await EnsureInitializedAsync(ct);
        await using var connection = await OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO node_runs (
                node_run_id, mission_id, node_id, node_kind, state, parent_node_run_id,
                iteration_no, input_json, output_json, error, started_at, completed_at)
            VALUES ($id, $mission, $node, $kind, $state, $parent, $iteration, $input, $output, $error, $started, $completed);
            """;
        command.Parameters.AddWithValue("$id", nodeRun.NodeRunId);
        command.Parameters.AddWithValue("$mission", nodeRun.MissionId);
        command.Parameters.AddWithValue("$node", nodeRun.NodeId);
        command.Parameters.AddWithValue("$kind", nodeRun.NodeKind.ToString());
        command.Parameters.AddWithValue("$state", nodeRun.State.ToString());
        command.Parameters.AddWithValue("$parent", (object?)nodeRun.ParentNodeRunId ?? DBNull.Value);
        command.Parameters.AddWithValue("$iteration", (object?)nodeRun.IterationNo ?? DBNull.Value);
        command.Parameters.AddWithValue("$input", (object?)nodeRun.InputJson ?? DBNull.Value);
        command.Parameters.AddWithValue("$output", (object?)nodeRun.OutputJson ?? DBNull.Value);
        command.Parameters.AddWithValue("$error", (object?)nodeRun.Error ?? DBNull.Value);
        command.Parameters.AddWithValue("$started", (object?)Format(nodeRun.StartedAt) ?? DBNull.Value);
        command.Parameters.AddWithValue("$completed", (object?)Format(nodeRun.CompletedAt) ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(ct);
    }

    public async Task<NodeRun?> GetNodeRunAsync(string nodeRunId, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        await using var connection = await OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = SelectNodeSql + " WHERE node_run_id = $id;";
        command.Parameters.AddWithValue("$id", nodeRunId);
        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? ReadNode(reader) : null;
    }

    public async Task<IReadOnlyList<NodeRun>> ListNodeRunsAsync(string missionId, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        await using var connection = await OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = SelectNodeSql + " WHERE mission_id = $mission ORDER BY started_at ASC;";
        command.Parameters.AddWithValue("$mission", missionId);
        var list = new List<NodeRun>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            list.Add(ReadNode(reader));
        }
        return list;
    }

    public async Task SetNodeRunStateAsync(string nodeRunId, NodeRunState state, string? outputJson = null, string? error = null, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        await using var connection = await OpenAsync(ct);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(ct);
        await using var currentCommand = connection.CreateCommand();
        currentCommand.Transaction = transaction;
        currentCommand.CommandText = "SELECT state FROM node_runs WHERE node_run_id = $id;";
        currentCommand.Parameters.AddWithValue("$id", nodeRunId);
        var currentValue = await currentCommand.ExecuteScalarAsync(ct) as string
            ?? throw new KeyNotFoundException($"Node run not found: '{nodeRunId}'.");
        NodeRunStateMachine.EnsureTransition(Enum.Parse<NodeRunState>(currentValue), state);
        await using var update = connection.CreateCommand();
        update.Transaction = transaction;
        update.CommandText = """
            UPDATE node_runs SET state = $state, output_json = $output, error = $error,
                started_at = CASE WHEN $state = 'Running' THEN COALESCE(started_at, $now) ELSE started_at END,
                completed_at = CASE WHEN $terminal = 1 THEN COALESCE(completed_at, $now) ELSE completed_at END
            WHERE node_run_id = $id;
            """;
        update.Parameters.AddWithValue("$state", state.ToString());
        update.Parameters.AddWithValue("$output", (object?)outputJson ?? DBNull.Value);
        update.Parameters.AddWithValue("$error", (object?)error ?? DBNull.Value);
        update.Parameters.AddWithValue("$terminal", state is NodeRunState.Succeeded or NodeRunState.Failed or NodeRunState.Skipped or NodeRunState.Unreached ? 1 : 0);
        update.Parameters.AddWithValue("$now", Format(DateTimeOffset.UtcNow));
        update.Parameters.AddWithValue("$id", nodeRunId);
        await update.ExecuteNonQueryAsync(ct);
        await transaction.CommitAsync(ct);
    }

    public async Task RecordEdgeTransitAsync(EdgeTransit transit, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(transit);
        await EnsureInitializedAsync(ct);
        await using var connection = await OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO edge_transits (edge_transit_id, mission_id, edge_id, from_node_run_id, to_node_run_id, condition_result, transited_at)
            VALUES ($id, $mission, $edge, $from, $to, $condition, $transited);
            """;
        command.Parameters.AddWithValue("$id", transit.EdgeTransitId);
        command.Parameters.AddWithValue("$mission", transit.MissionId);
        command.Parameters.AddWithValue("$edge", transit.EdgeId);
        command.Parameters.AddWithValue("$from", transit.FromNodeRunId);
        command.Parameters.AddWithValue("$to", transit.ToNodeRunId);
        command.Parameters.AddWithValue("$condition", (object?)transit.ConditionResult ?? DBNull.Value);
        command.Parameters.AddWithValue("$transited", Format(transit.TransitedAt));
        await command.ExecuteNonQueryAsync(ct);
    }

    public async Task<IReadOnlyList<EdgeTransit>> ListEdgeTransitsAsync(string missionId, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        await using var connection = await OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT edge_transit_id, mission_id, edge_id, from_node_run_id, to_node_run_id, condition_result, transited_at
            FROM edge_transits WHERE mission_id = $mission ORDER BY transited_at ASC;
            """;
        command.Parameters.AddWithValue("$mission", missionId);
        var list = new List<EdgeTransit>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            list.Add(new EdgeTransit
            {
                EdgeTransitId = reader.GetString(0),
                MissionId = reader.GetString(1),
                EdgeId = reader.GetString(2),
                FromNodeRunId = reader.GetString(3),
                ToNodeRunId = reader.GetString(4),
                ConditionResult = reader.IsDBNull(5) ? null : reader.GetString(5),
                TransitedAt = Parse(reader.GetString(6)),
            });
        }
        return list;
    }

    private const string SelectVersionSql = "SELECT graph_version_id, graph_name, version_no, content_hash, definition_yaml, created_at FROM graph_versions";
    private const string SelectNodeSql = "SELECT node_run_id, mission_id, node_id, node_kind, state, parent_node_run_id, iteration_no, input_json, output_json, error, started_at, completed_at FROM node_runs";

    private async Task InitializeAsync()
    {
        await using var connection = await OpenAsync(CancellationToken.None);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA journal_mode = WAL;
            CREATE TABLE IF NOT EXISTS graph_versions (
                graph_version_id TEXT NOT NULL PRIMARY KEY,
                graph_name TEXT NOT NULL,
                version_no INTEGER NOT NULL,
                content_hash TEXT NOT NULL,
                definition_yaml TEXT NOT NULL,
                created_at TEXT NOT NULL
            );
            CREATE UNIQUE INDEX IF NOT EXISTS ux_graph_versions_name_hash ON graph_versions(graph_name, content_hash);
            CREATE TABLE IF NOT EXISTS node_runs (
                node_run_id TEXT NOT NULL PRIMARY KEY,
                mission_id TEXT NOT NULL,
                node_id TEXT NOT NULL,
                node_kind TEXT NOT NULL,
                state TEXT NOT NULL,
                parent_node_run_id TEXT NULL,
                iteration_no INTEGER NULL,
                input_json TEXT NULL,
                output_json TEXT NULL,
                error TEXT NULL,
                started_at TEXT NULL,
                completed_at TEXT NULL
            );
            CREATE INDEX IF NOT EXISTS ix_node_runs_mission ON node_runs(mission_id, started_at);
            CREATE TABLE IF NOT EXISTS edge_transits (
                edge_transit_id TEXT NOT NULL PRIMARY KEY,
                mission_id TEXT NOT NULL,
                edge_id TEXT NOT NULL,
                from_node_run_id TEXT NOT NULL,
                to_node_run_id TEXT NOT NULL,
                condition_result TEXT NULL,
                transited_at TEXT NOT NULL
            );
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

    private static GraphVersion ReadVersion(SqliteDataReader reader) => new()
    {
        GraphVersionId = reader.GetString(0),
        GraphName = reader.GetString(1),
        VersionNo = reader.GetInt32(2),
        ContentHash = reader.GetString(3),
        DefinitionYaml = reader.GetString(4),
        CreatedAt = Parse(reader.GetString(5)),
    };

    private static NodeRun ReadNode(SqliteDataReader reader) => new()
    {
        NodeRunId = reader.GetString(0),
        MissionId = reader.GetString(1),
        NodeId = reader.GetString(2),
        NodeKind = Enum.Parse<NodeKind>(reader.GetString(3)),
        State = Enum.Parse<NodeRunState>(reader.GetString(4)),
        ParentNodeRunId = reader.IsDBNull(5) ? null : reader.GetString(5),
        IterationNo = reader.IsDBNull(6) ? null : reader.GetInt32(6),
        InputJson = reader.IsDBNull(7) ? null : reader.GetString(7),
        OutputJson = reader.IsDBNull(8) ? null : reader.GetString(8),
        Error = reader.IsDBNull(9) ? null : reader.GetString(9),
        StartedAt = reader.IsDBNull(10) ? null : Parse(reader.GetString(10)),
        CompletedAt = reader.IsDBNull(11) ? null : Parse(reader.GetString(11)),
    };

    private static string Format(DateTimeOffset value) => value.ToString("O", CultureInfo.InvariantCulture);
    private static string? Format(DateTimeOffset? value) => value?.ToString("O", CultureInfo.InvariantCulture);
    private static DateTimeOffset Parse(string value) => DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
}
