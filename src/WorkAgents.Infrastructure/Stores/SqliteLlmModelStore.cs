using Microsoft.Data.Sqlite;
using WorkAgents.Core;
using WorkAgents.Core.Abstractions;

namespace WorkAgents.Infrastructure.Stores;

/// <summary>LLMモデル設定とエージェント割当をSQLiteへ保存し、APIキーを保護ストアへ分離する。</summary>
public sealed class SqliteLlmModelStore : ILlmModelStore
{
    private readonly string _connectionString;
    private readonly ISecretStore _secretStore;
    private readonly Task _initialization;

    public SqliteLlmModelStore(string databasePath, ISecretStore secretStore)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        _secretStore = secretStore;

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

    public async Task<IReadOnlyList<LlmModelSettings>> ListAsync(CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        await using var connection = await OpenConnectionAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = $"{SelectColumns} ORDER BY is_default DESC, name COLLATE NOCASE;";

        var result = new List<LlmModelSettings>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            result.Add(Read(reader));
        }
        return result;
    }

    public async Task<LlmModelSettings?> GetAsync(string id, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        await EnsureInitializedAsync(ct);
        await using var connection = await OpenConnectionAsync(ct);
        var settings = await ReadOneAsync(connection, "WHERE id = $value", id, ct);
        return settings is null ? null : await WithSecretAsync(settings, ct);
    }

    public async Task<LlmModelSettings?> ResolveForAgentAsync(string agentName, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentName);
        await EnsureInitializedAsync(ct);
        await using var connection = await OpenConnectionAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            {SelectColumns}
            LEFT JOIN agent_model_assignments a
                ON a.model_id = llm_models.id AND a.agent_name = $agent_name
            WHERE a.agent_name IS NOT NULL OR llm_models.is_default = 1
            ORDER BY CASE WHEN a.agent_name IS NOT NULL THEN 0 ELSE 1 END
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$agent_name", agentName);
        await using var reader = await command.ExecuteReaderAsync(ct);
        var settings = await reader.ReadAsync(ct) ? Read(reader) : null;
        return settings is null ? null : await WithSecretAsync(settings, ct);
    }

    public async Task SaveAsync(LlmModelSettings settings, string? apiKey, string? clientSecret = null, CancellationToken ct = default)
    {
        Validate(settings);
        await EnsureInitializedAsync(ct);

        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            await _secretStore.SetAsync(SecretName(settings.Id), apiKey, ct);
        }
        if (!string.IsNullOrWhiteSpace(clientSecret))
        {
            await _secretStore.SetAsync(ClientSecretName(settings.Id), clientSecret, ct);
        }

        await using var connection = await OpenConnectionAsync(ct);
        await using var transaction = connection.BeginTransaction();
        var isFirst = await CountAsync(connection, transaction, ct) == 0;
        var isDefault = settings.IsDefault || isFirst;
        if (isDefault)
        {
            await ExecuteAsync(connection, transaction, "UPDATE llm_models SET is_default = 0;", ct);
        }

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO llm_models (
                id, name, provider, project_endpoint, endpoint, deployment_name, api,
                is_default, has_api_key, tenant_id, client_id, has_client_secret,
                max_context_window_tokens, max_output_tokens,
                compaction_trigger_tokens, compaction_target_tokens, compaction_minimum_preserved_groups)
            VALUES (
                $id, $name, $provider, $project_endpoint, $endpoint, $deployment_name, $api,
                $is_default, $has_api_key, $tenant_id, $client_id, $has_client_secret,
                $max_context, $max_output,
                $trigger, $target, $preserved)
            ON CONFLICT(id) DO UPDATE SET
                name = excluded.name,
                provider = excluded.provider,
                project_endpoint = excluded.project_endpoint,
                endpoint = excluded.endpoint,
                deployment_name = excluded.deployment_name,
                api = excluded.api,
                is_default = excluded.is_default,
                has_api_key = CASE WHEN excluded.has_api_key = 1 THEN 1 ELSE llm_models.has_api_key END,
                tenant_id = excluded.tenant_id,
                client_id = excluded.client_id,
                has_client_secret = CASE WHEN excluded.has_client_secret = 1 THEN 1 ELSE llm_models.has_client_secret END,
                max_context_window_tokens = excluded.max_context_window_tokens,
                max_output_tokens = excluded.max_output_tokens,
                compaction_trigger_tokens = excluded.compaction_trigger_tokens,
                compaction_target_tokens = excluded.compaction_target_tokens,
                compaction_minimum_preserved_groups = excluded.compaction_minimum_preserved_groups;
            """;
        command.Parameters.AddWithValue("$id", settings.Id.Trim());
        command.Parameters.AddWithValue("$name", settings.Name.Trim());
        command.Parameters.AddWithValue("$provider", settings.Provider.ToString());
        command.Parameters.AddWithValue("$project_endpoint", settings.ProjectEndpoint.Trim());
        command.Parameters.AddWithValue("$endpoint", settings.Endpoint.Trim());
        command.Parameters.AddWithValue("$deployment_name", settings.DeploymentName.Trim());
        command.Parameters.AddWithValue("$api", settings.Api);
        command.Parameters.AddWithValue("$is_default", isDefault);
        command.Parameters.AddWithValue("$has_api_key", !string.IsNullOrWhiteSpace(apiKey) || settings.HasApiKey);
        command.Parameters.AddWithValue("$tenant_id", settings.TenantId.Trim());
        command.Parameters.AddWithValue("$client_id", settings.ClientId.Trim());
        command.Parameters.AddWithValue("$has_client_secret", !string.IsNullOrWhiteSpace(clientSecret) || settings.HasClientSecret);
        command.Parameters.AddWithValue("$max_context", settings.MaxContextWindowTokens);
        command.Parameters.AddWithValue("$max_output", settings.MaxOutputTokens);
        command.Parameters.AddWithValue("$trigger", settings.CompactionTriggerTokens);
        command.Parameters.AddWithValue("$target", settings.CompactionTargetTokens);
        command.Parameters.AddWithValue("$preserved", settings.CompactionMinimumPreservedGroups);
        await command.ExecuteNonQueryAsync(ct);
                await ExecuteAsync(connection, transaction, """
                        UPDATE llm_models SET is_default = 1
                        WHERE id = (SELECT id FROM llm_models ORDER BY name COLLATE NOCASE LIMIT 1)
                            AND NOT EXISTS (SELECT 1 FROM llm_models WHERE is_default = 1);
                        """, ct);
        await transaction.CommitAsync(ct);
    }

    public async Task DeleteAsync(string id, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        await EnsureInitializedAsync(ct);
        await using var connection = await OpenConnectionAsync(ct);
        await using var transaction = connection.BeginTransaction();
        await ExecuteAsync(connection, transaction,
            "DELETE FROM agent_model_assignments WHERE model_id = $value;", ct, id);
        await ExecuteAsync(connection, transaction, "DELETE FROM llm_models WHERE id = $value;", ct, id);
        await ExecuteAsync(connection, transaction, """
            UPDATE llm_models SET is_default = 1
            WHERE id = (SELECT id FROM llm_models ORDER BY name COLLATE NOCASE LIMIT 1)
              AND NOT EXISTS (SELECT 1 FROM llm_models WHERE is_default = 1);
            """, ct);
        await transaction.CommitAsync(ct);
    }

    public async Task<string?> GetAgentModelIdAsync(string agentName, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentName);
        await EnsureInitializedAsync(ct);
        await using var connection = await OpenConnectionAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT model_id FROM agent_model_assignments WHERE agent_name = $agent_name;";
        command.Parameters.AddWithValue("$agent_name", agentName);
        return await command.ExecuteScalarAsync(ct) as string;
    }

    public async Task AssignAgentAsync(string agentName, string? modelId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentName);
        await EnsureInitializedAsync(ct);
        await using var connection = await OpenConnectionAsync(ct);
        await using var command = connection.CreateCommand();
        if (string.IsNullOrWhiteSpace(modelId))
        {
            command.CommandText = "DELETE FROM agent_model_assignments WHERE agent_name = $agent_name;";
        }
        else
        {
            command.CommandText = """
                INSERT INTO agent_model_assignments (agent_name, model_id)
                SELECT $agent_name, id FROM llm_models WHERE id = $model_id
                ON CONFLICT(agent_name) DO UPDATE SET model_id = excluded.model_id;
                """;
            command.Parameters.AddWithValue("$model_id", modelId);
        }
        command.Parameters.AddWithValue("$agent_name", agentName);
        if (await command.ExecuteNonQueryAsync(ct) == 0 && !string.IsNullOrWhiteSpace(modelId))
        {
            throw new KeyNotFoundException($"LLM model '{modelId}' was not found.");
        }
    }

    private async Task InitializeAsync()
    {
        await using var connection = await OpenConnectionAsync(CancellationToken.None);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA journal_mode = WAL;
            CREATE TABLE IF NOT EXISTS llm_models (
                id TEXT NOT NULL PRIMARY KEY,
                name TEXT NOT NULL,
                provider TEXT NOT NULL,
                project_endpoint TEXT NOT NULL,
                endpoint TEXT NOT NULL,
                deployment_name TEXT NOT NULL,
                api TEXT NOT NULL,
                is_default INTEGER NOT NULL,
                has_api_key INTEGER NOT NULL,
                max_context_window_tokens INTEGER NOT NULL,
                max_output_tokens INTEGER NOT NULL,
                compaction_trigger_tokens INTEGER NOT NULL,
                compaction_target_tokens INTEGER NOT NULL,
                compaction_minimum_preserved_groups INTEGER NOT NULL
            );
            CREATE UNIQUE INDEX IF NOT EXISTS ux_llm_models_default
                ON llm_models(is_default) WHERE is_default = 1;
            CREATE TABLE IF NOT EXISTS agent_model_assignments (
                agent_name TEXT NOT NULL PRIMARY KEY,
                model_id TEXT NOT NULL
            );
            """;
        await command.ExecuteNonQueryAsync();
        await AddColumnIfMissingAsync(connection, "tenant_id", "TEXT NOT NULL DEFAULT ''");
        await AddColumnIfMissingAsync(connection, "client_id", "TEXT NOT NULL DEFAULT ''");
        await AddColumnIfMissingAsync(connection, "has_client_secret", "INTEGER NOT NULL DEFAULT 0");
    }

    private static async Task AddColumnIfMissingAsync(SqliteConnection connection, string columnName, string columnDefinition)
    {
        await using var probe = connection.CreateCommand();
        probe.CommandText = "SELECT COUNT(*) FROM pragma_table_info('llm_models') WHERE name = $column;";
        probe.Parameters.AddWithValue("$column", columnName);
        var exists = (long)(await probe.ExecuteScalarAsync() ?? 0L) > 0;
        if (exists)
        {
            return;
        }

        await using var alter = connection.CreateCommand();
        alter.CommandText = $"ALTER TABLE llm_models ADD COLUMN {columnName} {columnDefinition};";
        await alter.ExecuteNonQueryAsync();
    }

    private async Task<LlmModelSettings?> ReadOneAsync(
        SqliteConnection connection, string where, string value, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"{SelectColumns} {where} LIMIT 1;";
        command.Parameters.AddWithValue("$value", value);
        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? Read(reader) : null;
    }

    private async Task<LlmModelSettings> WithSecretAsync(LlmModelSettings settings, CancellationToken ct)
    {
        var apiKey = settings.HasApiKey ? await _secretStore.GetAsync(SecretName(settings.Id), ct) : null;
        var clientSecret = settings.HasClientSecret ? await _secretStore.GetAsync(ClientSecretName(settings.Id), ct) : null;
        return new LlmModelSettings
        {
            Id = settings.Id,
            Name = settings.Name,
            Provider = settings.Provider,
            ProjectEndpoint = settings.ProjectEndpoint,
            Endpoint = settings.Endpoint,
            DeploymentName = settings.DeploymentName,
            Api = settings.Api,
            IsDefault = settings.IsDefault,
            HasApiKey = settings.HasApiKey,
            ApiKey = apiKey,
            TenantId = settings.TenantId,
            ClientId = settings.ClientId,
            HasClientSecret = settings.HasClientSecret,
            ClientSecret = clientSecret,
            MaxContextWindowTokens = settings.MaxContextWindowTokens,
            MaxOutputTokens = settings.MaxOutputTokens,
            CompactionTriggerTokens = settings.CompactionTriggerTokens,
            CompactionTargetTokens = settings.CompactionTargetTokens,
            CompactionMinimumPreservedGroups = settings.CompactionMinimumPreservedGroups,
        };
    }

    private static LlmModelSettings Read(SqliteDataReader reader) => new()
    {
        Id = reader.GetString(0),
        Name = reader.GetString(1),
        Provider = Enum.Parse<LlmProvider>(reader.GetString(2), ignoreCase: true),
        ProjectEndpoint = reader.GetString(3),
        Endpoint = reader.GetString(4),
        DeploymentName = reader.GetString(5),
        Api = reader.GetString(6),
        IsDefault = reader.GetBoolean(7),
        HasApiKey = reader.GetBoolean(8),
        TenantId = reader.GetString(9),
        ClientId = reader.GetString(10),
        HasClientSecret = reader.GetBoolean(11),
        MaxContextWindowTokens = reader.GetInt32(12),
        MaxOutputTokens = reader.GetInt32(13),
        CompactionTriggerTokens = reader.GetInt32(14),
        CompactionTargetTokens = reader.GetInt32(15),
        CompactionMinimumPreservedGroups = reader.GetInt32(16),
    };

    private static void Validate(LlmModelSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentException.ThrowIfNullOrWhiteSpace(settings.Id);
        ArgumentException.ThrowIfNullOrWhiteSpace(settings.Name);
        ArgumentException.ThrowIfNullOrWhiteSpace(settings.DeploymentName);
        // Anthropic、OpenAI、OpenRouterは公式の既定エンドポイントを使うため、Endpoint入力は任意。
        if (settings.Provider == LlmProvider.AmazonBedrock)
        {
            if (string.IsNullOrWhiteSpace(settings.Endpoint))
            {
                throw new ArgumentException("An AWS region is required for the AmazonBedrock provider.", nameof(settings));
            }
        }
        else if (settings.Provider is not (LlmProvider.Anthropic or LlmProvider.OpenAI or LlmProvider.OpenRouter))
        {
            var endpoint = settings.Provider == LlmProvider.Foundry ? settings.ProjectEndpoint : settings.Endpoint;
            if (!Uri.TryCreate(endpoint, UriKind.Absolute, out _))
            {
                throw new ArgumentException("A valid absolute endpoint is required.", nameof(settings));
            }
        }
        if (settings.MaxContextWindowTokens <= 0 || settings.MaxOutputTokens <= 0 ||
            settings.CompactionTriggerTokens <= 0 || settings.CompactionTargetTokens <= 0 ||
            settings.CompactionMinimumPreservedGroups <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(settings), "Token limits must be positive.");
        }
    }

    private async Task EnsureInitializedAsync(CancellationToken ct) => await _initialization.WaitAsync(ct);

    private async Task<SqliteConnection> OpenConnectionAsync(CancellationToken ct)
    {
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(ct);
        return connection;
    }

    private static async Task<long> CountAsync(
        SqliteConnection connection, SqliteTransaction transaction, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT COUNT(*) FROM llm_models;";
        return (long)(await command.ExecuteScalarAsync(ct) ?? 0L);
    }

    private static async Task ExecuteAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string sql,
        CancellationToken ct,
        string? value = null)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        if (value is not null)
        {
            command.Parameters.AddWithValue("$value", value);
        }
        await command.ExecuteNonQueryAsync(ct);
    }

    private static string SecretName(string id) => $"llm-model-{id}-api-key";

    private static string ClientSecretName(string id) => $"llm-model-{id}-client-secret";

    private const string SelectColumns = """
        SELECT id, name, provider, project_endpoint, endpoint, deployment_name, api,
               is_default, has_api_key, tenant_id, client_id, has_client_secret,
               max_context_window_tokens, max_output_tokens,
               compaction_trigger_tokens, compaction_target_tokens, compaction_minimum_preserved_groups
        FROM llm_models
        """;
}
