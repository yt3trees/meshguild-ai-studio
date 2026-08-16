using System.Globalization;
using Microsoft.Data.Sqlite;
using WorkAgents.Core.Abstractions;
using WorkAgents.Core.Missions;

namespace WorkAgents.Infrastructure.Stores;

/// <summary>Standalone budget store sharing the mission budget schema.</summary>
public sealed class SqliteBudgetStore : IBudgetStore
{
    private readonly string _connectionString;
    private readonly Task _initialization;

    public SqliteBudgetStore(string databasePath)
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

    public async Task UpsertAsync(Budget budget, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(budget);
        await EnsureInitializedAsync(ct);
        await using var connection = await OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO budgets (
                mission_id, cost_limit_usd, time_limit_seconds, max_iterations, max_concurrent_agents,
                cost_used_usd, elapsed_seconds, iterations_used, peak_concurrent_agents)
            VALUES ($mission, $cost_limit, $time_limit, $max_iterations, $max_agents,
                    $cost_used, $elapsed, $iterations, $peak)
            ON CONFLICT(mission_id) DO UPDATE SET
                cost_limit_usd = excluded.cost_limit_usd,
                time_limit_seconds = excluded.time_limit_seconds,
                max_iterations = excluded.max_iterations,
                max_concurrent_agents = excluded.max_concurrent_agents,
                cost_used_usd = excluded.cost_used_usd,
                elapsed_seconds = excluded.elapsed_seconds,
                iterations_used = excluded.iterations_used,
                peak_concurrent_agents = excluded.peak_concurrent_agents;
            """;
        command.Parameters.AddWithValue("$mission", budget.MissionId);
        command.Parameters.AddWithValue("$cost_limit", (object?)budget.CostLimitUsd ?? DBNull.Value);
        command.Parameters.AddWithValue("$time_limit", (object?)budget.TimeLimitSeconds ?? DBNull.Value);
        command.Parameters.AddWithValue("$max_iterations", (object?)budget.MaxIterations ?? DBNull.Value);
        command.Parameters.AddWithValue("$max_agents", (object?)budget.MaxConcurrentAgents ?? DBNull.Value);
        command.Parameters.AddWithValue("$cost_used", budget.CostUsedUsd);
        command.Parameters.AddWithValue("$elapsed", budget.ElapsedSeconds);
        command.Parameters.AddWithValue("$iterations", budget.IterationsUsed);
        command.Parameters.AddWithValue("$peak", budget.PeakConcurrentAgents);
        await command.ExecuteNonQueryAsync(ct);
    }

    public async Task<Budget?> GetAsync(string missionId, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        await using var connection = await OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT mission_id, cost_limit_usd, time_limit_seconds, max_iterations, max_concurrent_agents,
                   cost_used_usd, elapsed_seconds, iterations_used, peak_concurrent_agents
            FROM budgets WHERE mission_id = $mission;
            """;
        command.Parameters.AddWithValue("$mission", missionId);
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
        {
            return null;
        }
        return new Budget
        {
            MissionId = reader.GetString(0),
            CostLimitUsd = reader.IsDBNull(1) ? null : reader.GetDouble(1),
            TimeLimitSeconds = reader.IsDBNull(2) ? null : reader.GetInt32(2),
            MaxIterations = reader.IsDBNull(3) ? null : reader.GetInt32(3),
            MaxConcurrentAgents = reader.IsDBNull(4) ? null : reader.GetInt32(4),
            CostUsedUsd = reader.GetDouble(5),
            ElapsedSeconds = reader.GetInt32(6),
            IterationsUsed = reader.GetInt32(7),
            PeakConcurrentAgents = reader.GetInt32(8),
        };
    }

    private async Task InitializeAsync()
    {
        await using var connection = await OpenAsync(CancellationToken.None);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS budgets (
                mission_id TEXT NOT NULL PRIMARY KEY,
                cost_limit_usd REAL NULL,
                time_limit_seconds INTEGER NULL,
                max_iterations INTEGER NULL,
                max_concurrent_agents INTEGER NULL,
                cost_used_usd REAL NOT NULL DEFAULT 0,
                elapsed_seconds INTEGER NOT NULL DEFAULT 0,
                iterations_used INTEGER NOT NULL DEFAULT 0,
                peak_concurrent_agents INTEGER NOT NULL DEFAULT 0
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
}
