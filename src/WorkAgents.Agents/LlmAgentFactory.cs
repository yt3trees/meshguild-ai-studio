using System.ClientModel;
using Amazon;
using Amazon.BedrockRuntime;
using Anthropic;
using Azure;
using Azure.AI.OpenAI;
using Azure.AI.Projects;
using Azure.Core;
using Azure.Identity;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Compaction;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OpenAI;
using OpenAI.Chat;
using OpenAI.Responses;
using WorkAgents.Agents.Loading;
using WorkAgents.Agents.Tools;
using WorkAgents.Core;
using WorkAgents.Harness.Harness;

namespace WorkAgents.Agents;

/// <summary>
/// <see cref="LlmModelSettings"/> と <see cref="AgentDefinition"/> から MAF の <c>AIAgent</c> を構築する(M1〜M2)。
/// <list type="bullet">
/// <item>M1: プロバイダ種別(FoundryのEntra ID/APIキー経路、OpenAI、Amazon Bedrock、OpenRouter、既存AzureOpenAI、Anthropic、GitHub Models)に応じて同期ステートレス AIAgent を構築。</item>
/// <item>M2: <c>harness.shell=true</c> または <c>harness.fileStore=workspace</c> のとき <see cref="HarnessAgentFactory"/> 経由で
///   FileStore + LocalShellExecutor(denylist+confine)を付与した HarnessAgent を構築する。Harness 不要なら M1 と同じ ChatClientAgent を返す。</item>
/// </list>
/// <c>AIProjectClient</c> / <c>AzureOpenAIClient</c> は構築後にキャッシュする。
/// Harness 必須エージェントは <c>AsHarnessAgent</c>(<see cref="IChatClient"/>) 経由で構築するため、
/// 内部で ChatClientAgent を組み立てて <c>.ChatClient</c> を取り出し Harness に渡す。
/// </summary>
public sealed class LlmAgentFactory
{
    private readonly ILogger<LlmAgentFactory> _logger;
    private readonly HarnessAgentFactory? _harnessFactory;
    private readonly ILoggerFactory? _loggerFactory;
    private readonly AgentToolCatalog _toolCatalog;

    public LlmAgentFactory(
        ILogger<LlmAgentFactory> logger,
        HarnessAgentFactory? harnessFactory = null,
        ILoggerFactory? loggerFactory = null,
        AgentToolCatalog? toolCatalog = null)
    {
        _logger = logger;
        _harnessFactory = harnessFactory;
        _loggerFactory = loggerFactory;
        _toolCatalog = toolCatalog ?? AgentToolCatalog.Empty;
    }

    /// <summary>エージェント定義とWeb管理されたモデル設定から <c>AIAgent</c> を構築する。</summary>
    public AIAgent Create(AgentDefinition definition, LlmModelSettings settings)
        => Create(definition, settings, workingDirectory: null);

    /// <summary>エージェント定義からrun専用作業ディレクトリ付きのエージェントを構築する(M3)。</summary>
    public AIAgent Create(AgentDefinition definition, LlmModelSettings settings, string? workingDirectory)
    {
        var tools = _toolCatalog.GetTools(definition.Name).ToList();
        if (NeedsHarness(definition))
        {
            return CreateHarness(definition, settings, workingDirectory, tools);
        }

        return settings.Provider switch
        {
            LlmProvider.Foundry => AddCompaction(CreateFoundryBase(definition, settings, tools), definition, settings, tools),
            LlmProvider.OpenAI => AddCompaction(CreateOpenAIBase(definition, settings, tools), definition, settings, tools),
            LlmProvider.AmazonBedrock => AddCompaction(CreateBedrockBase(definition, settings, tools), definition, settings, tools),
            LlmProvider.OpenRouter => AddCompaction(CreateOpenRouterBase(definition, settings, tools), definition, settings, tools),
            LlmProvider.AzureOpenAI => AddCompaction(CreateAzureOpenAIBase(definition, settings, tools), definition, settings, tools),
            LlmProvider.Anthropic => AddCompaction(CreateAnthropicBase(definition, settings, tools), definition, settings, tools),
            LlmProvider.GitHubModels => AddCompaction(CreateGitHubModelsBase(definition, settings, tools), definition, settings, tools),
            _ => throw new InvalidOperationException($"unknown LLM provider: {settings.Provider}"),
        };
    }

    private static bool NeedsHarness(AgentDefinition def)
        => def.HarnessShell
            || string.Equals(def.HarnessFileStore, "workspace", StringComparison.OrdinalIgnoreCase)
            || def.LocalSkillNames.Count > 0
            || def.SharedSkillNames.Count > 0;

    private AIAgent CreateHarness(
        AgentDefinition def,
        LlmModelSettings settings,
        string? workingDirectory,
        IReadOnlyList<AITool> tools)
    {
        if (_harnessFactory is null)
        {
            throw new InvalidOperationException(
                $"agent '{def.Name}' requires harness (shell/workspace), but HarnessAgentFactory is not registered. " +
                "Call AddWorkAgentsAgents with harness enabled (Profile: Local|Azure).");
        }

        var chatClient = ResolveChatClient(def, settings, tools);
        var config = new HarnessAgentConfig
        {
            Name = def.Name,
            Instructions = def.Instructions,
            AgentFolderPath = def.FolderPath,
            FileStoreKind = def.HarnessFileStore,
            ShellEnabled = def.HarnessShell,
            SkillPaths = ResolveSkillPaths(def),
            WorkingDirectory = workingDirectory,
            MaxContextWindowTokens = settings.MaxContextWindowTokens,
            MaxOutputTokens = settings.MaxOutputTokens,
            CompactionTriggerTokens = settings.CompactionTriggerTokens,
            CompactionTargetTokens = settings.CompactionTargetTokens,
            CompactionMinimumPreservedGroups = settings.CompactionMinimumPreservedGroups,
            CustomTools = tools,
        };
        _logger.LogInformation("building harness agent name={Name} fileStore={FileStore} shell={Shell} skillCount={SkillCount}",
            def.Name, def.HarnessFileStore ?? "(none)", def.HarnessShell, config.SkillPaths.Count);
        return _harnessFactory.Create(chatClient, config, _loggerFactory);
    }

    private static IReadOnlyList<string> ResolveSkillPaths(AgentDefinition definition)
    {
        var paths = definition.LocalSkillNames
            .Select(name => Path.Combine(definition.FolderPath, "skills", name))
            .ToList();
        var localNames = new HashSet<string>(definition.LocalSkillNames, StringComparer.OrdinalIgnoreCase);
        var sharedSkillsRoot = Path.GetFullPath(Path.Combine(definition.FolderPath, "..", "..", "skills"));
        paths.AddRange(definition.SharedSkillNames
            .Where(name => !localNames.Contains(name))
            .Select(name => definition.SharedSkillPaths.TryGetValue(name, out var resolvedPath)
                ? resolvedPath
                : Path.Combine(sharedSkillsRoot, name)));
        return paths;
    }

    /// <summary>
    /// Harness 構築用に内部 ChatClientAgent を1つ組み立て、その <c>.ChatClient</c>(<see cref="IChatClient"/>)を取り出す。
    /// 取り出した IChatClient を <c>AsHarnessAgent</c> に渡す。Agent 本体は捨てる(ステートレス同期を前提)。
    /// </summary>
    private IChatClient ResolveChatClient(
        AgentDefinition def,
        LlmModelSettings settings,
        IReadOnlyList<AITool> tools)
    {
        ChatClientAgent agent = settings.Provider switch
        {
            LlmProvider.Foundry => (ChatClientAgent)CreateFoundryBase(def, settings, tools),
            LlmProvider.OpenAI => (ChatClientAgent)CreateOpenAIBase(def, settings, tools),
            LlmProvider.AmazonBedrock => (ChatClientAgent)CreateBedrockBase(def, settings, tools),
            LlmProvider.OpenRouter => (ChatClientAgent)CreateOpenRouterBase(def, settings, tools),
            LlmProvider.AzureOpenAI => (ChatClientAgent)CreateAzureOpenAIBase(def, settings, tools),
            LlmProvider.Anthropic => (ChatClientAgent)CreateAnthropicBase(def, settings, tools),
            LlmProvider.GitHubModels => (ChatClientAgent)CreateGitHubModelsBase(def, settings, tools),
            _ => throw new InvalidOperationException($"unknown LLM provider: {settings.Provider}"),
        };

        var chatClient = agent.ChatClient
            ?? throw new InvalidOperationException(
                $"MAF ChatClientAgent for provider '{settings.Provider}' did not expose an IChatClient for harness construction.");
        return chatClient;
    }

    private AIAgent CreateFoundryBase(
        AgentDefinition def,
        LlmModelSettings settings,
        IReadOnlyList<AITool> tools)
    {
        if (string.IsNullOrWhiteSpace(settings.ProjectEndpoint))
        {
            throw new InvalidOperationException("A project endpoint is required for the Foundry provider.");
        }

        if (!string.IsNullOrWhiteSpace(settings.ApiKey))
        {
            // Foundry API keys are supported by the project's OpenAI-compatible Responses endpoint.
            var openAiClient = new OpenAIClient(
                new ApiKeyCredential(settings.ApiKey),
                new OpenAIClientOptions { Endpoint = FoundryOpenAIEndpoint(settings.ProjectEndpoint) });

            return openAiClient.GetResponsesClient().AsAIAgent(
                model: settings.DeploymentName,
                instructions: def.Instructions,
                name: def.Name,
                description: null,
                tools: tools.ToList(),
                clientFactory: null,
                loggerFactory: _loggerFactory ?? NullLoggerFactory.Instance,
                services: null);
        }

        var client = new AIProjectClient(new Uri(settings.ProjectEndpoint), BuildFoundryCredential(settings));

        return client.AsAIAgent(
            model: settings.DeploymentName,
            instructions: def.Instructions,
            name: def.Name,
            description: null,
            tools: tools.ToList(),
            clientFactory: null,
            loggerFactory: _loggerFactory ?? NullLoggerFactory.Instance,
            services: null);
    }

    /// <summary>
    /// テナントID・クライアントID・クライアントシークレットが揃っていればサービスプリンシパル認証
    /// (<see cref="ClientSecretCredential"/>) を使う。APIキーが入力されている場合は
    /// <see cref="CreateFoundryBase"/> 側でプロジェクトの OpenAI互換 Responses APIへ接続するため、
    /// ここは使わない。サービスプリンシパルが揃っていなければ <see cref="DefaultAzureCredential"/>
    /// にフォールバックする(開発機では az login、本番はマネージドID等)。
    /// </summary>
    private static TokenCredential BuildFoundryCredential(LlmModelSettings settings)
    {
        if (!string.IsNullOrWhiteSpace(settings.TenantId)
            && !string.IsNullOrWhiteSpace(settings.ClientId)
            && !string.IsNullOrWhiteSpace(settings.ClientSecret))
        {
            return new ClientSecretCredential(settings.TenantId, settings.ClientId, settings.ClientSecret);
        }

        return new DefaultAzureCredential();
    }

    private static Uri FoundryOpenAIEndpoint(string projectEndpoint)
    {
        var endpoint = projectEndpoint.TrimEnd('/');
        if (endpoint.EndsWith("/responses", StringComparison.OrdinalIgnoreCase))
        {
            // Azure AI Foundryのポータルは「/openai/v1/responses」までを対象URIとして表示するため、
            // 末尾に貼り付けられがちな "/responses" を取り除いてから正規化する。
            endpoint = endpoint[..^"/responses".Length].TrimEnd('/');
        }
        if (!endpoint.EndsWith("/openai/v1", StringComparison.OrdinalIgnoreCase))
        {
            endpoint += "/openai/v1";
        }

        return new Uri($"{endpoint}/");
    }

    /// <summary>OpenAI公式API経由で <c>AIAgent</c> を構築する。</summary>
    private AIAgent CreateOpenAIBase(
        AgentDefinition def,
        LlmModelSettings settings,
        IReadOnlyList<AITool> tools)
    {
        if (string.IsNullOrWhiteSpace(settings.ApiKey))
        {
            throw new InvalidOperationException("An API key is required for the OpenAI provider.");
        }

        // Endpointを指定しない場合はOpenAI公式の https://api.openai.com/v1 を使う。
        var client = new OpenAIClient(new ApiKeyCredential(settings.ApiKey));
        if (string.Equals(settings.Api, "Responses", StringComparison.OrdinalIgnoreCase))
        {
            return client.GetResponsesClient().AsAIAgent(
                model: settings.DeploymentName,
                instructions: def.Instructions,
                name: def.Name,
                description: null,
                tools: tools.ToList(),
                clientFactory: null,
                loggerFactory: _loggerFactory ?? NullLoggerFactory.Instance,
                services: null);
        }

        return client.GetChatClient(settings.DeploymentName).AsAIAgent(
            instructions: def.Instructions,
            name: def.Name,
            description: null,
            tools: tools.ToList(),
            clientFactory: null,
            loggerFactory: _loggerFactory ?? NullLoggerFactory.Instance,
            services: null);
    }

    /// <summary>Amazon Bedrock Converse API経由で <c>AIAgent</c> を構築する。</summary>
    private AIAgent CreateBedrockBase(
        AgentDefinition def,
        LlmModelSettings settings,
        IReadOnlyList<AITool> tools)
    {
        if (string.IsNullOrWhiteSpace(settings.Endpoint))
        {
            throw new InvalidOperationException("An AWS region is required for the AmazonBedrock provider.");
        }

        // AWS SDKの標準認証チェーン(env/profile/role等)を使い、AWS秘密鍵をアプリへ取り込まない。
        var runtime = new AmazonBedrockRuntimeClient(RegionEndpoint.GetBySystemName(settings.Endpoint));
        var chatClient = runtime.AsIChatClient(settings.DeploymentName);

        return new ChatClientAgent(
            chatClient,
            new ChatClientAgentOptions
            {
                Id = def.Name,
                Name = def.Name,
                ChatOptions = new ChatOptions
                {
                    Instructions = def.Instructions,
                    MaxOutputTokens = settings.MaxOutputTokens,
                    Tools = tools.ToList(),
                },
                RequirePerServiceCallChatHistoryPersistence = true,
                UseProvidedChatClientAsIs = true,
            },
            _loggerFactory ?? NullLoggerFactory.Instance,
            services: null);
    }

    /// <summary>OpenRouterのOpenAI互換Chat Completions API経由で <c>AIAgent</c> を構築する。</summary>
    private AIAgent CreateOpenRouterBase(
        AgentDefinition def,
        LlmModelSettings settings,
        IReadOnlyList<AITool> tools)
    {
        if (string.IsNullOrWhiteSpace(settings.ApiKey))
        {
            throw new InvalidOperationException("An API key is required for the OpenRouter provider.");
        }

        var client = new OpenAIClient(
            new ApiKeyCredential(settings.ApiKey),
            new OpenAIClientOptions { Endpoint = new Uri("https://openrouter.ai/api/v1/") });

        return client.GetChatClient(settings.DeploymentName).AsAIAgent(
            instructions: def.Instructions,
            name: def.Name,
            description: null,
            tools: tools.ToList(),
            clientFactory: null,
            loggerFactory: _loggerFactory ?? NullLoggerFactory.Instance,
            services: null);
    }

    private AIAgent CreateAzureOpenAIBase(
        AgentDefinition def,
        LlmModelSettings settings,
        IReadOnlyList<AITool> tools)
    {
        if (string.IsNullOrWhiteSpace(settings.Endpoint))
        {
            throw new InvalidOperationException("An endpoint is required for the AzureOpenAI provider.");
        }

        var client = BuildAzureOpenAIClient(settings);

        if (string.Equals(settings.Api, "Responses", StringComparison.OrdinalIgnoreCase))
        {
            return client.GetResponsesClient().AsAIAgent(
                model: settings.DeploymentName,
                instructions: def.Instructions,
                name: def.Name,
                description: null,
                tools: tools.ToList(),
                clientFactory: null,
                loggerFactory: _loggerFactory ?? NullLoggerFactory.Instance,
                services: null);
        }

        // Chat Completion(既定): broad なモデル互換性。deployment 名を ChatClient に渡す。
        return client.GetChatClient(settings.DeploymentName).AsAIAgent(
            instructions: def.Instructions,
            name: def.Name,
            description: null,
            tools: tools.ToList(),
            clientFactory: null,
            loggerFactory: _loggerFactory ?? NullLoggerFactory.Instance,
            services: null);
    }

    /// <summary>
    /// Anthropic API(Claude モデル)経由で <c>AIAgent</c> を構築する。
    /// <c>Anthropic</c> NuGet パッケージが提供する <c>IChatClient</c> 統合(<c>AsIChatClient</c>)を使い、
    /// 他プロバイダと同様に <see cref="ChatClientAgent"/> でラップする(Foundry/AzureOpenAI と違い
    /// <c>Microsoft.Agents.AI.OpenAI</c> 側の <c>AsAIAgent</c> 拡張は使えないため手組みする)。
    /// </summary>
    private AIAgent CreateAnthropicBase(
        AgentDefinition def,
        LlmModelSettings settings,
        IReadOnlyList<AITool> tools)
    {
        if (string.IsNullOrWhiteSpace(settings.ApiKey))
        {
            throw new InvalidOperationException("An API key is required for the Anthropic provider.");
        }

        var client = new AnthropicClient { ApiKey = settings.ApiKey };
        var chatClient = client.AsIChatClient(settings.DeploymentName, settings.MaxOutputTokens);

        return new ChatClientAgent(
            chatClient,
            new ChatClientAgentOptions
            {
                Id = def.Name,
                Name = def.Name,
                ChatOptions = new ChatOptions
                {
                    Instructions = def.Instructions,
                    MaxOutputTokens = settings.MaxOutputTokens,
                    Tools = tools.ToList(),
                },
                RequirePerServiceCallChatHistoryPersistence = true,
                UseProvidedChatClientAsIs = true,
            },
            _loggerFactory ?? NullLoggerFactory.Instance,
            services: null);
    }

    /// <summary>
    /// GitHub Models(GitHub Copilot と同じモデル基盤)経由で <c>AIAgent</c> を構築する。
    /// OpenAI 互換の Chat Completions エンドポイントのため、素の <c>OpenAI.OpenAIClient</c> に
    /// GitHub Models のエンドポイントを指定し、認証は GitHub PAT(<c>models: read</c> 権限)を使う。
    /// </summary>
    private AIAgent CreateGitHubModelsBase(
        AgentDefinition def,
        LlmModelSettings settings,
        IReadOnlyList<AITool> tools)
    {
        if (string.IsNullOrWhiteSpace(settings.ApiKey))
        {
            throw new InvalidOperationException(
                "A GitHub personal access token (with the 'models: read' permission) is required for the GitHubModels provider.");
        }
        if (string.IsNullOrWhiteSpace(settings.Endpoint))
        {
            throw new InvalidOperationException("An endpoint is required for the GitHubModels provider.");
        }

        var client = new OpenAIClient(
            new ApiKeyCredential(settings.ApiKey),
            new OpenAIClientOptions { Endpoint = new Uri(settings.Endpoint) });

        return client.GetChatClient(settings.DeploymentName).AsAIAgent(
            instructions: def.Instructions,
            name: def.Name,
            description: null,
            tools: tools.ToList(),
            clientFactory: null,
            loggerFactory: _loggerFactory ?? NullLoggerFactory.Instance,
            services: null);
    }

    private AIAgent AddCompaction(
        AIAgent baseAgent,
        AgentDefinition definition,
        LlmModelSettings settings,
        IReadOnlyList<AITool> tools)
    {
        if (baseAgent is not ChatClientAgent chatClientAgent)
        {
            throw new InvalidOperationException(
                $"Agent '{definition.Name}' does not expose a ChatClientAgent for session compaction.");
        }

        var chatClient = chatClientAgent.ChatClient
            ?? throw new InvalidOperationException(
                $"Agent '{definition.Name}' does not expose a chat client for session compaction.");
        var strategy = CreateSummarizationStrategy(chatClient, settings);
        var options = new ChatClientAgentOptions
        {
            Id = definition.Name,
            Name = definition.Name,
            ChatOptions = new ChatOptions
            {
                Instructions = definition.Instructions,
                MaxOutputTokens = settings.MaxOutputTokens,
                Tools = tools.ToList(),
            },
            AIContextProviders =
            [
                new CompactionProvider(strategy, $"work-agents.compaction.{definition.Name}", _loggerFactory),
            ],
            RequirePerServiceCallChatHistoryPersistence = true,
            UseProvidedChatClientAsIs = true,
        };

        return new ChatClientAgent(
            chatClient,
            options,
            _loggerFactory ?? NullLoggerFactory.Instance,
            services: null);
    }

    private static SummarizationCompactionStrategy CreateSummarizationStrategy(
        IChatClient chatClient,
        LlmModelSettings settings)
    {
        return new SummarizationCompactionStrategy(
            chatClient,
            CompactionTriggers.TokensExceed(settings.CompactionTriggerTokens),
            settings.CompactionMinimumPreservedGroups,
            summarizationPrompt: null,
            target: CompactionTriggers.TokensBelow(settings.CompactionTargetTokens));
    }

    private AzureOpenAIClient BuildAzureOpenAIClient(LlmModelSettings settings)
    {
        var uri = new Uri(settings.Endpoint);
        _logger.LogInformation("building AzureOpenAIClient endpoint={Endpoint} auth={Auth}",
            settings.Endpoint, string.IsNullOrEmpty(settings.ApiKey) ? "DefaultAzureCredential" : "ApiKey");

        if (!string.IsNullOrEmpty(settings.ApiKey))
        {
            return new AzureOpenAIClient(uri, new AzureKeyCredential(settings.ApiKey));
        }

        // 本番では DefaultAzureCredential を避け ManagedIdentityCredential を明示指定(第9章)。
        return new AzureOpenAIClient(uri, new DefaultAzureCredential());
    }

}
