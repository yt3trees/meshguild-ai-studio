using System.Globalization;
using Microsoft.Data.Sqlite;
using WorkAgents.Core.Abstractions;
using WorkAgents.Core.Loops;

namespace WorkAgents.Infrastructure.Stores;

/// <summary>SQLite persistence for loop runs, iterations, evaluations, and metrics.</summary>
public sealed class SqliteLoopStore : ILoopStore
{
    private readonly string _connectionString;
    private readonly ISecretRedactor? _redactor;
    private readonly Task _initialization;

    public SqliteLoopStore(string databasePath, ISecretRedactor? redactor = null)
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

    public async Task CreateLoopRunAsync(LoopRun loopRun, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(loopRun);
        await EnsureInitializedAsync(ct);
        await using var connection = await OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO loop_runs (
                loop_run_id, mission_id, node_run_id, max_iterations, cost_limit_usd,
                time_limit_seconds, score_threshold, stop_reason, best_iteration_id,
                started_at, completed_at)
            VALUES ($id, $mission, $node, $max, $cost, $time, $score, $reason, $best, $started, $completed);
            """;
        command.Parameters.AddWithValue("$id", loopRun.LoopRunId);
        command.Parameters.AddWithValue("$mission", loopRun.MissionId);
        command.Parameters.AddWithValue("$node", loopRun.NodeRunId);
        command.Parameters.AddWithValue("$max", loopRun.MaxIterations);
        command.Parameters.AddWithValue("$cost", (object?)loopRun.CostLimitUsd ?? DBNull.Value);
        command.Parameters.AddWithValue("$time", (object?)loopRun.TimeLimitSeconds ?? DBNull.Value);
        command.Parameters.AddWithValue("$score", (object?)loopRun.ScoreThreshold ?? DBNull.Value);
        command.Parameters.AddWithValue("$reason", (object?)loopRun.StopReason?.ToString() ?? DBNull.Value);
        command.Parameters.AddWithValue("$best", (object?)loopRun.BestIterationId ?? DBNull.Value);
        command.Parameters.AddWithValue("$started", Format(loopRun.StartedAt));
        command.Parameters.AddWithValue("$completed", (object?)Format(loopRun.CompletedAt) ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(ct);
    }

    public async Task<LoopRun?> GetLoopRunAsync(string loopRunId, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        await using var connection = await OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = SelectLoopSql + " WHERE loop_run_id = $id;";
        command.Parameters.AddWithValue("$id", loopRunId);
        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? ReadLoop(reader) : null;
    }

    public async Task<IReadOnlyList<LoopRun>> ListLoopRunsAsync(string missionId, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        await using var connection = await OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = SelectLoopSql + " WHERE mission_id = $mission ORDER BY started_at ASC;";
        command.Parameters.AddWithValue("$mission", missionId);
        var list = new List<LoopRun>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            list.Add(ReadLoop(reader));
        }
        return list;
    }

    public async Task CompleteLoopRunAsync(string loopRunId, LoopStopReason stopReason, string? bestIterationId, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        await using var connection = await OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE loop_runs SET stop_reason = $reason, best_iteration_id = $best, completed_at = $completed
            WHERE loop_run_id = $id;
            """;
        command.Parameters.AddWithValue("$reason", stopReason.ToString());
        command.Parameters.AddWithValue("$best", (object?)bestIterationId ?? DBNull.Value);
        command.Parameters.AddWithValue("$completed", Format(DateTimeOffset.UtcNow));
        command.Parameters.AddWithValue("$id", loopRunId);
        await command.ExecuteNonQueryAsync(ct);
    }

    public async Task CreateIterationAsync(Iteration iteration, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(iteration);
        await EnsureInitializedAsync(ct);
        await using var connection = await OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO iterations (
                iteration_id, loop_run_id, iteration_no, input_json, output_json, state,
                cost_usd, tokens, duration_ms, discarded_at, started_at, completed_at)
            VALUES ($id, $loop, $no, $input, $output, $state, $cost, $tokens, $duration, $discarded, $started, $completed);
            """;
        command.Parameters.AddWithValue("$id", iteration.IterationId);
        command.Parameters.AddWithValue("$loop", iteration.LoopRunId);
        command.Parameters.AddWithValue("$no", iteration.IterationNo);
        command.Parameters.AddWithValue("$input", (object?)iteration.InputJson ?? DBNull.Value);
        command.Parameters.AddWithValue("$output", (object?)iteration.OutputJson ?? DBNull.Value);
        command.Parameters.AddWithValue("$state", iteration.State.ToString());
        command.Parameters.AddWithValue("$cost", iteration.CostUsd);
        command.Parameters.AddWithValue("$tokens", iteration.Tokens);
        command.Parameters.AddWithValue("$duration", iteration.DurationMs);
        command.Parameters.AddWithValue("$discarded", (object?)Format(iteration.DiscardedAt) ?? DBNull.Value);
        command.Parameters.AddWithValue("$started", Format(iteration.StartedAt));
        command.Parameters.AddWithValue("$completed", (object?)Format(iteration.CompletedAt) ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(ct);
    }

    public async Task<IReadOnlyList<Iteration>> ListIterationsAsync(string loopRunId, bool includeDiscarded = false, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        await using var connection = await OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = SelectIterationSql + (includeDiscarded ? "" : " AND discarded_at IS NULL") + " ORDER BY iteration_no ASC, started_at ASC;";
        command.Parameters.AddWithValue("$loop", loopRunId);
        var list = new List<Iteration>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            list.Add(ReadIteration(reader));
        }
        return list;
    }

    public async Task CompleteIterationAsync(string iterationId, IterationState state, string? outputJson, double costUsd, long tokens, long durationMs, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        await using var connection = await OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE iterations SET state = $state, output_json = $output, cost_usd = $cost,
                tokens = $tokens, duration_ms = $duration, completed_at = $completed
            WHERE iteration_id = $id;
            """;
        command.Parameters.AddWithValue("$state", state.ToString());
        command.Parameters.AddWithValue("$output", (object?)outputJson ?? DBNull.Value);
        command.Parameters.AddWithValue("$cost", costUsd);
        command.Parameters.AddWithValue("$tokens", tokens);
        command.Parameters.AddWithValue("$duration", durationMs);
        command.Parameters.AddWithValue("$completed", Format(DateTimeOffset.UtcNow));
        command.Parameters.AddWithValue("$id", iterationId);
        await command.ExecuteNonQueryAsync(ct);
    }

    public async Task DiscardIterationsAfterAsync(string loopRunId, int iterationNo, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        await using var connection = await OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE iterations SET state = $state, discarded_at = $discarded
            WHERE loop_run_id = $loop AND iteration_no > $no AND discarded_at IS NULL;
            """;
        command.Parameters.AddWithValue("$state", IterationState.Discarded.ToString());
        command.Parameters.AddWithValue("$discarded", Format(DateTimeOffset.UtcNow));
        command.Parameters.AddWithValue("$loop", loopRunId);
        command.Parameters.AddWithValue("$no", iterationNo);
        await command.ExecuteNonQueryAsync(ct);
    }

    public async Task AddEvaluationAsync(Evaluation evaluation, IReadOnlyList<EvaluationMetric> metrics, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(evaluation);
        if (evaluation.Score is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(evaluation), "Evaluation score must be between 0 and 1.");
        }
        await EnsureInitializedAsync(ct);
        await using var connection = await OpenAsync(ct);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(ct);
        await using var evaluationCommand = connection.CreateCommand();
        evaluationCommand.Transaction = transaction;
        evaluationCommand.CommandText = """
            INSERT INTO evaluations (evaluation_id, iteration_id, score, evaluator_kind, evaluator_ref, notes, passed, created_at)
            VALUES ($id, $iteration, $score, $kind, $ref, $notes, $passed, $created);
            """;
        evaluationCommand.Parameters.AddWithValue("$id", evaluation.EvaluationId);
        evaluationCommand.Parameters.AddWithValue("$iteration", evaluation.IterationId);
        evaluationCommand.Parameters.AddWithValue("$score", evaluation.Score);
        evaluationCommand.Parameters.AddWithValue("$kind", evaluation.EvaluatorKind.ToString());
        evaluationCommand.Parameters.AddWithValue("$ref", evaluation.EvaluatorRef);
        evaluationCommand.Parameters.AddWithValue("$notes", (object?)(_redactor is null || evaluation.Notes is null ? evaluation.Notes : await _redactor.RedactAsync(evaluation.Notes, ct)) ?? DBNull.Value);
        evaluationCommand.Parameters.AddWithValue("$passed", evaluation.Passed ? 1 : 0);
        evaluationCommand.Parameters.AddWithValue("$created", Format(evaluation.CreatedAt));
        await evaluationCommand.ExecuteNonQueryAsync(ct);
        foreach (var metric in metrics)
        {
            await using var metricCommand = connection.CreateCommand();
            metricCommand.Transaction = transaction;
            metricCommand.CommandText = """
                INSERT INTO evaluation_metrics (metric_id, evaluation_id, name, value, target, achieved, unit)
                VALUES ($id, $evaluation, $name, $value, $target, $achieved, $unit);
                """;
            metricCommand.Parameters.AddWithValue("$id", metric.MetricId);
            metricCommand.Parameters.AddWithValue("$evaluation", evaluation.EvaluationId);
            metricCommand.Parameters.AddWithValue("$name", metric.Name);
            metricCommand.Parameters.AddWithValue("$value", metric.Value);
            metricCommand.Parameters.AddWithValue("$target", metric.Target);
            metricCommand.Parameters.AddWithValue("$achieved", metric.Achieved ? 1 : 0);
            metricCommand.Parameters.AddWithValue("$unit", (object?)metric.Unit ?? DBNull.Value);
            await metricCommand.ExecuteNonQueryAsync(ct);
        }
        await transaction.CommitAsync(ct);
    }

    public async Task<Evaluation?> GetEvaluationAsync(string iterationId, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        await using var connection = await OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT evaluation_id, iteration_id, score, evaluator_kind, evaluator_ref, notes, passed, created_at
            FROM evaluations WHERE iteration_id = $iteration;
            """;
        command.Parameters.AddWithValue("$iteration", iterationId);
        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? ReadEvaluation(reader) : null;
    }

    public async Task<IReadOnlyList<EvaluationMetric>> ListMetricsAsync(string evaluationId, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        await using var connection = await OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT metric_id, evaluation_id, name, value, target, achieved, unit
            FROM evaluation_metrics WHERE evaluation_id = $evaluation ORDER BY name ASC;
            """;
        command.Parameters.AddWithValue("$evaluation", evaluationId);
        var list = new List<EvaluationMetric>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            list.Add(new EvaluationMetric
            {
                MetricId = reader.GetString(0),
                EvaluationId = reader.GetString(1),
                Name = reader.GetString(2),
                Value = reader.GetDouble(3),
                Target = reader.GetDouble(4),
                Achieved = reader.GetInt32(5) != 0,
                Unit = reader.IsDBNull(6) ? null : reader.GetString(6),
            });
        }
        return list;
    }

    private const string SelectLoopSql = """
        SELECT loop_run_id, mission_id, node_run_id, max_iterations, cost_limit_usd,
               time_limit_seconds, score_threshold, stop_reason, best_iteration_id,
               started_at, completed_at FROM loop_runs
        """;
    private const string SelectIterationSql = """
        SELECT iteration_id, loop_run_id, iteration_no, input_json, output_json, state,
               cost_usd, tokens, duration_ms, discarded_at, started_at, completed_at
        FROM iterations WHERE loop_run_id = $loop
        """;

    private async Task InitializeAsync()
    {
        await using var connection = await OpenAsync(CancellationToken.None);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA journal_mode = WAL;
            CREATE TABLE IF NOT EXISTS loop_runs (
                loop_run_id TEXT NOT NULL PRIMARY KEY,
                mission_id TEXT NOT NULL,
                node_run_id TEXT NOT NULL,
                max_iterations INTEGER NOT NULL,
                cost_limit_usd REAL NULL,
                time_limit_seconds INTEGER NULL,
                score_threshold REAL NULL,
                stop_reason TEXT NULL,
                best_iteration_id TEXT NULL,
                started_at TEXT NOT NULL,
                completed_at TEXT NULL
            );
            CREATE TABLE IF NOT EXISTS iterations (
                iteration_id TEXT NOT NULL PRIMARY KEY,
                loop_run_id TEXT NOT NULL,
                iteration_no INTEGER NOT NULL,
                input_json TEXT NULL,
                output_json TEXT NULL,
                state TEXT NOT NULL,
                cost_usd REAL NOT NULL DEFAULT 0,
                tokens INTEGER NOT NULL DEFAULT 0,
                duration_ms INTEGER NOT NULL DEFAULT 0,
                discarded_at TEXT NULL,
                started_at TEXT NOT NULL,
                completed_at TEXT NULL
            );
            CREATE UNIQUE INDEX IF NOT EXISTS ux_iterations_active_no
                ON iterations(loop_run_id, iteration_no) WHERE discarded_at IS NULL;
            CREATE TABLE IF NOT EXISTS evaluations (
                evaluation_id TEXT NOT NULL PRIMARY KEY,
                iteration_id TEXT NOT NULL UNIQUE,
                score REAL NOT NULL,
                evaluator_kind TEXT NOT NULL,
                evaluator_ref TEXT NOT NULL,
                notes TEXT NULL,
                passed INTEGER NOT NULL,
                created_at TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS evaluation_metrics (
                metric_id TEXT NOT NULL PRIMARY KEY,
                evaluation_id TEXT NOT NULL,
                name TEXT NOT NULL,
                value REAL NOT NULL,
                target REAL NOT NULL,
                achieved INTEGER NOT NULL,
                unit TEXT NULL
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

    private static LoopRun ReadLoop(SqliteDataReader reader) => new()
    {
        LoopRunId = reader.GetString(0),
        MissionId = reader.GetString(1),
        NodeRunId = reader.GetString(2),
        MaxIterations = reader.GetInt32(3),
        CostLimitUsd = reader.IsDBNull(4) ? null : reader.GetDouble(4),
        TimeLimitSeconds = reader.IsDBNull(5) ? null : reader.GetInt32(5),
        ScoreThreshold = reader.IsDBNull(6) ? null : reader.GetDouble(6),
        StopReason = reader.IsDBNull(7) ? null : Enum.Parse<LoopStopReason>(reader.GetString(7)),
        BestIterationId = reader.IsDBNull(8) ? null : reader.GetString(8),
        StartedAt = Parse(reader.GetString(9)),
        CompletedAt = reader.IsDBNull(10) ? null : Parse(reader.GetString(10)),
    };

    private static Iteration ReadIteration(SqliteDataReader reader) => new()
    {
        IterationId = reader.GetString(0),
        LoopRunId = reader.GetString(1),
        IterationNo = reader.GetInt32(2),
        InputJson = reader.IsDBNull(3) ? null : reader.GetString(3),
        OutputJson = reader.IsDBNull(4) ? null : reader.GetString(4),
        State = Enum.Parse<IterationState>(reader.GetString(5)),
        CostUsd = reader.GetDouble(6),
        Tokens = reader.GetInt64(7),
        DurationMs = reader.GetInt64(8),
        DiscardedAt = reader.IsDBNull(9) ? null : Parse(reader.GetString(9)),
        StartedAt = Parse(reader.GetString(10)),
        CompletedAt = reader.IsDBNull(11) ? null : Parse(reader.GetString(11)),
    };

    private static Evaluation ReadEvaluation(SqliteDataReader reader) => new()
    {
        EvaluationId = reader.GetString(0),
        IterationId = reader.GetString(1),
        Score = reader.GetDouble(2),
        EvaluatorKind = Enum.Parse<EvaluatorKind>(reader.GetString(3)),
        EvaluatorRef = reader.GetString(4),
        Notes = reader.IsDBNull(5) ? null : reader.GetString(5),
        Passed = reader.GetInt32(6) != 0,
        CreatedAt = Parse(reader.GetString(7)),
    };

    private static string Format(DateTimeOffset value) => value.ToString("O", CultureInfo.InvariantCulture);

    private static string? Format(DateTimeOffset? value) => value?.ToString("O", CultureInfo.InvariantCulture);

    private static DateTimeOffset Parse(string value)
        => DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
}
