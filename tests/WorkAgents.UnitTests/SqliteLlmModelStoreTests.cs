using WorkAgents.Core;
using WorkAgents.Core.Abstractions;
using WorkAgents.Infrastructure.Stores;

namespace WorkAgents.UnitTests;

public sealed class SqliteLlmModelStoreTests
{
    [Fact]
    public async Task SaveAsync_FirstModelBecomesDefault_WhenIsDefaultIsFalse()
    {
        var databasePath = CreateDatabasePath();
        try
        {
            var store = new SqliteLlmModelStore(databasePath, new TestSecretStore());

            await store.SaveAsync(CreateSettings("model-1", "First model", isDefault: false), apiKey: null);

            var model = Assert.Single(await store.ListAsync());
            Assert.True(model.IsDefault);
        }
        finally
        {
            DeleteDatabaseDirectory(databasePath);
        }
    }

    [Fact]
    public async Task SaveAsync_OpenAIModel_DoesNotRequireAnEndpoint()
    {
        var databasePath = CreateDatabasePath();
        try
        {
            var store = new SqliteLlmModelStore(databasePath, new TestSecretStore());
            await store.SaveAsync(new LlmModelSettings
            {
                Id = "openai-model",
                Name = "OpenAI model",
                Provider = LlmProvider.OpenAI,
                DeploymentName = "gpt-4.1",
            }, apiKey: "test-api-key");

            var model = await store.GetAsync("openai-model");
            Assert.NotNull(model);
            Assert.Equal(LlmProvider.OpenAI, model.Provider);
            Assert.Empty(model.Endpoint);
            Assert.Equal("test-api-key", model.ApiKey);
        }
        finally
        {
            DeleteDatabaseDirectory(databasePath);
        }
    }

    [Fact]
    public async Task SaveAsync_OpenRouterModel_DoesNotRequireAnEndpoint()
    {
        var databasePath = CreateDatabasePath();
        try
        {
            var store = new SqliteLlmModelStore(databasePath, new TestSecretStore());
            await store.SaveAsync(new LlmModelSettings
            {
                Id = "openrouter-model",
                Name = "OpenRouter model",
                Provider = LlmProvider.OpenRouter,
                DeploymentName = "openai/gpt-4.1",
            }, apiKey: "test-api-key");

            var model = await store.GetAsync("openrouter-model");
            Assert.NotNull(model);
            Assert.Equal(LlmProvider.OpenRouter, model.Provider);
            Assert.Empty(model.Endpoint);
            Assert.Equal("test-api-key", model.ApiKey);
        }
        finally
        {
            DeleteDatabaseDirectory(databasePath);
        }
    }

    [Fact]
    public async Task SaveAsync_AmazonBedrockModel_StoresTheAwsRegion()
    {
        var databasePath = CreateDatabasePath();
        try
        {
            var store = new SqliteLlmModelStore(databasePath, new TestSecretStore());
            await store.SaveAsync(new LlmModelSettings
            {
                Id = "bedrock-model",
                Name = "Amazon Bedrock model",
                Provider = LlmProvider.AmazonBedrock,
                Endpoint = "us-east-1",
                DeploymentName = "amazon.nova-lite-v1:0",
            }, apiKey: null);

            var model = await store.GetAsync("bedrock-model");
            Assert.NotNull(model);
            Assert.Equal(LlmProvider.AmazonBedrock, model.Provider);
            Assert.Equal("us-east-1", model.Endpoint);
            Assert.False(model.HasApiKey);
        }
        finally
        {
            DeleteDatabaseDirectory(databasePath);
        }
    }

    [Fact]
    public async Task ResolveForAgentAsync_ExplicitAssignmentOverridesDefault_AndRemovalRestoresDefault()
    {
        var databasePath = CreateDatabasePath();
        try
        {
            var store = new SqliteLlmModelStore(databasePath, new TestSecretStore());
            await store.SaveAsync(CreateSettings("default-model", "Default model", isDefault: true), apiKey: null);
            await store.SaveAsync(CreateSettings("assigned-model", "Assigned model"), apiKey: null);

            await store.AssignAgentAsync("repo-agent", "assigned-model");

            var assigned = await store.ResolveForAgentAsync("repo-agent");
            Assert.NotNull(assigned);
            Assert.Equal("assigned-model", assigned.Id);
            Assert.Equal("assigned-model", await store.GetAgentModelIdAsync("repo-agent"));

            await store.AssignAgentAsync("repo-agent", modelId: null);

            var fallback = await store.ResolveForAgentAsync("repo-agent");
            Assert.NotNull(fallback);
            Assert.Equal("default-model", fallback.Id);
            Assert.Null(await store.GetAgentModelIdAsync("repo-agent"));
        }
        finally
        {
            DeleteDatabaseDirectory(databasePath);
        }
    }

    [Fact]
    public async Task ApiKey_IsExcludedFromList_AndResolvedThroughSecretStore()
    {
        var databasePath = CreateDatabasePath();
        try
        {
            var secretStore = new TestSecretStore();
            var store = new SqliteLlmModelStore(databasePath, secretStore);

            await store.SaveAsync(CreateSettings("secured-model", "Secured model"), "test-api-key");

            var listed = Assert.Single(await store.ListAsync());
            Assert.True(listed.HasApiKey);
            Assert.Null(listed.ApiKey);

            var resolved = await store.ResolveForAgentAsync("unassigned-agent");
            Assert.NotNull(resolved);
            Assert.True(resolved.HasApiKey);
            Assert.Equal("test-api-key", resolved.ApiKey);
            Assert.Contains("llm-model-secured-model-api-key", secretStore.RequestedNames);
        }
        finally
        {
            DeleteDatabaseDirectory(databasePath);
        }
    }

    [Fact]
    public async Task DeleteAsync_DefaultModelTransfersDefaultAndRemovesItsAssignment()
    {
        var databasePath = CreateDatabasePath();
        try
        {
            var store = new SqliteLlmModelStore(databasePath, new TestSecretStore());
            await store.SaveAsync(CreateSettings("deleted-model", "Deleted model", isDefault: true), apiKey: null);
            await store.SaveAsync(CreateSettings("remaining-model", "Remaining model"), apiKey: null);
            await store.AssignAgentAsync("repo-agent", "deleted-model");

            await store.DeleteAsync("deleted-model");

            var remaining = Assert.Single(await store.ListAsync());
            Assert.Equal("remaining-model", remaining.Id);
            Assert.True(remaining.IsDefault);
            Assert.Null(await store.GetAgentModelIdAsync("repo-agent"));

            var resolved = await store.ResolveForAgentAsync("repo-agent");
            Assert.NotNull(resolved);
            Assert.Equal("remaining-model", resolved.Id);
        }
        finally
        {
            DeleteDatabaseDirectory(databasePath);
        }
    }

    [Fact]
    public async Task ClientSecret_IsExcludedFromList_AndResolvedThroughSecretStore()
    {
        var databasePath = CreateDatabasePath();
        try
        {
            var secretStore = new TestSecretStore();
            var store = new SqliteLlmModelStore(databasePath, secretStore);
            var settings = CreateSettings("sp-model", "Service principal model", tenantId: "tenant-1", clientId: "client-1");

            await store.SaveAsync(settings, apiKey: null, clientSecret: "test-client-secret");

            var listed = Assert.Single(await store.ListAsync());
            Assert.Equal("tenant-1", listed.TenantId);
            Assert.Equal("client-1", listed.ClientId);
            Assert.True(listed.HasClientSecret);
            Assert.Null(listed.ClientSecret);

            var resolved = await store.ResolveForAgentAsync("unassigned-agent");
            Assert.NotNull(resolved);
            Assert.True(resolved.HasClientSecret);
            Assert.Equal("test-client-secret", resolved.ClientSecret);
            Assert.Contains("llm-model-sp-model-client-secret", secretStore.RequestedNames);
        }
        finally
        {
            DeleteDatabaseDirectory(databasePath);
        }
    }

    [Fact]
    public async Task SaveAsync_UpdatingWithoutClientSecret_PreservesExistingSecretFlag()
    {
        var databasePath = CreateDatabasePath();
        try
        {
            var secretStore = new TestSecretStore();
            var store = new SqliteLlmModelStore(databasePath, secretStore);
            var settings = CreateSettings("sp-model", "Service principal model");

            await store.SaveAsync(settings, apiKey: null, clientSecret: "initial-secret");
            var afterFirstSave = await store.GetAsync("sp-model");
            Assert.NotNull(afterFirstSave);

            await store.SaveAsync(
                new LlmModelSettings
                {
                    Id = afterFirstSave.Id,
                    Name = "Renamed",
                    Provider = afterFirstSave.Provider,
                    ProjectEndpoint = afterFirstSave.ProjectEndpoint,
                    Endpoint = afterFirstSave.Endpoint,
                    DeploymentName = afterFirstSave.DeploymentName,
                    Api = afterFirstSave.Api,
                    IsDefault = afterFirstSave.IsDefault,
                    HasApiKey = afterFirstSave.HasApiKey,
                    TenantId = afterFirstSave.TenantId,
                    ClientId = afterFirstSave.ClientId,
                    HasClientSecret = afterFirstSave.HasClientSecret,
                    MaxContextWindowTokens = afterFirstSave.MaxContextWindowTokens,
                    MaxOutputTokens = afterFirstSave.MaxOutputTokens,
                    CompactionTriggerTokens = afterFirstSave.CompactionTriggerTokens,
                    CompactionTargetTokens = afterFirstSave.CompactionTargetTokens,
                    CompactionMinimumPreservedGroups = afterFirstSave.CompactionMinimumPreservedGroups,
                },
                apiKey: null,
                clientSecret: null);

            var reloaded = await store.GetAsync("sp-model");
            Assert.NotNull(reloaded);
            Assert.True(reloaded.HasClientSecret);
            Assert.Equal("initial-secret", reloaded.ClientSecret);
        }
        finally
        {
            DeleteDatabaseDirectory(databasePath);
        }
    }

    private static LlmModelSettings CreateSettings(
        string id, string name, bool isDefault = false, string tenantId = "", string clientId = "") => new()
    {
        Id = id,
        Name = name,
        ProjectEndpoint = "https://example.test/projects/work-agents",
        DeploymentName = "test-deployment",
        IsDefault = isDefault,
        TenantId = tenantId,
        ClientId = clientId,
    };

    private static string CreateDatabasePath() => Path.Combine(
        Path.GetTempPath(),
        "work-agents-tests",
        $"{Guid.NewGuid():N}",
        "llm-models.db");

    private static void DeleteDatabaseDirectory(string databasePath)
    {
        var directory = Path.GetDirectoryName(databasePath);
        if (directory is not null && Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private sealed class TestSecretStore : ISecretStore
    {
        private readonly Dictionary<string, string> _values = [];

        public List<string> RequestedNames { get; } = [];

        public Task<string?> GetAsync(string name, CancellationToken ct = default)
        {
            RequestedNames.Add(name);
            return Task.FromResult(_values.GetValueOrDefault(name));
        }

        public Task SetAsync(string name, string value, CancellationToken ct = default)
        {
            _values[name] = value;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<string>> ListAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<string>>(_values.Keys.OrderBy(k => k, StringComparer.Ordinal).ToList());

        public Task<bool> DeleteAsync(string name, CancellationToken ct = default)
            => Task.FromResult(_values.Remove(name));
    }
}
