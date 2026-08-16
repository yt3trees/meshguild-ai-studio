using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WorkAgents.Agents.Configuration;
using WorkAgents.Agents.Loading;
using WorkAgents.Core;
using WorkAgents.Core.Abstractions;
using WorkAgents.Harness.Harness;
using WorkAgents.Agents.Tools;
using WorkAgents.Agents.Invocation;
using WorkAgents.Core.Teams;
using WorkAgents.Core.Graphs;

namespace WorkAgents.Agents.DependencyInjection;

/// <summary>
/// WorkAgents.Agents の DI 登録口。
/// <list type="bullet">
/// <item>LLMモデル設定は <see cref="ILlmModelStore"/> から実行時に解決する。</item>
/// <item><see cref="HarnessAgentFactory"/> を <see cref="ProfileOptions"/> 経由で登録(M2〜。harness.shell=true のエージェントで必要)。</item>
/// <item><see cref="FileBasedAgentLoader"/> を <c>Agents:DefinitionSources</c>(未設定時は
/// 開発時の出力フォルダー、配布時の共通 <c>definitions/</c> を標準ソースとして構成し、Agent定義を一度だけ読み込む
/// (specs/006-team-config-distribution)。</item>
/// <item><see cref="AgentToolCatalog"/> をSingletonで構築し、Agent専用Providerを起動時に検証する。</item>
/// <item><see cref="IAgentRegistry"/> をシングルトンで登録する。</item>
/// </list>
/// APIキーは <see cref="ISecretStore"/>、その他のモデル設定は <see cref="ILlmModelStore"/> に保存する。
/// </summary>
public static class AgentsServiceCollectionExtensions
{
    public static IServiceCollection AddWorkAgentsAgents(this IServiceCollection services, IConfiguration configuration)
    {
        // ProfileOptions は Infrastructure で登録される前提。無ければ Local 既定でフォールバック(単体テスト等)。
        services.TryAddSingleton<ProfileOptions>();
        services.AddSingleton<AgentsOptions>(sp => BuildAgentsOptions(configuration));
        services.AddSingleton<IToolPluginHostAllowlist>(sp =>
            new ToolPluginHostAllowlist(sp.GetRequiredService<AgentsOptions>().ToolPlugins.AllowedHosts));
        services.AddSingleton<HarnessAgentFactory>(sp =>
        {
            var profile = sp.GetRequiredService<ProfileOptions>();
            return new HarnessAgentFactory(
                profile,
                sp.GetService<ILogger<HarnessAgentFactory>>(),
                sp.GetService<WorkAgents.Harness.GitAuth.IGitAuth>());
        });
        services.AddSingleton<HarnessApprovalBridge>();

        services.AddSingleton<LlmAgentFactory>(sp =>
        {
            var logger = sp.GetRequiredService<ILogger<LlmAgentFactory>>();
            var harness = sp.GetService<HarnessAgentFactory>();
            var lf = sp.GetService<ILoggerFactory>();
            var toolCatalog = sp.GetRequiredService<AgentToolCatalog>();
            return new LlmAgentFactory(logger, harness, lf, toolCatalog);
        });
        services.AddSingleton<FileBasedAgentLoader>(sp =>
        {
            var root = FileBasedAgentLoader.ResolveAgentsRoot(AppContext.BaseDirectory);
            return new FileBasedAgentLoader(root, sp.GetService<ILogger<FileBasedAgentLoader>>());
        });
        services.AddSingleton<IReadOnlyList<AgentDefinition>>(sp =>
        {
            var sources = sp.GetRequiredService<AgentsOptions>().DefinitionSources;
            return sp.GetRequiredService<FileBasedAgentLoader>()
                .LoadFromSources(sources, sp.GetService<ILogger<DefinitionSourceResolver>>());
        });
        services.AddSingleton<AgentToolCatalog>(sp =>
        {
            var definitions = sp.GetRequiredService<IReadOnlyList<AgentDefinition>>();
            var toolPluginDirectories = sp.GetRequiredService<AgentsOptions>().ToolPluginDirectories;
            var catalog = AgentToolCatalog.CreateWithPlugins(sp, definitions, toolPluginDirectories, sp.GetService<ILogger<AgentToolCatalog>>());
            var teamLoader = new FileBasedTeamLoader(sp.GetService<ILogger<FileBasedTeamLoader>>());
            var sources = sp.GetRequiredService<AgentsOptions>().DefinitionSources;
            var knownAgents = definitions.Select(definition => definition.Name).ToArray();
            var teams = teamLoader.LoadAllFromSources(sources, knownAgents, sp.GetService<ILogger<DefinitionSourceResolver>>());
            var conversationTools = new ConversationToolProvider();
            foreach (var team in teams)
            {
                foreach (var registration in conversationTools.CreateTools(team.Orchestrator.Agent, orchestrator: true))
                {
                    catalog.AddRegistration(team.Orchestrator.Agent, registration);
                }
                foreach (var member in team.Members)
                {
                    foreach (var registration in conversationTools.CreateTools(member.Agent, orchestrator: false))
                    {
                        catalog.AddRegistration(member.Agent, registration);
                    }
                }
            }
            return catalog;
        });
        services.AddSingleton<FileBasedWorkflowLoader>(sp =>
        {
            var root = FileBasedWorkflowLoader.ResolveWorkflowsRoot(AppContext.BaseDirectory);
            return new FileBasedWorkflowLoader(root, sp.GetService<ILogger<FileBasedWorkflowLoader>>());
        });
        services.AddSingleton<FileBasedTeamLoader>();
        services.AddSingleton<IReadOnlyList<TeamDefinition>>(sp =>
        {
            var loader = sp.GetRequiredService<FileBasedTeamLoader>();
            var sources = sp.GetRequiredService<AgentsOptions>().DefinitionSources;
            var knownAgents = sp.GetRequiredService<IReadOnlyList<AgentDefinition>>().Select(def => def.Name).ToArray();
            return loader.LoadAllFromSources(sources, knownAgents, sp.GetService<ILogger<DefinitionSourceResolver>>());
        });
        services.AddSingleton<FileBasedGraphLoader>();
        services.AddSingleton<GraphYamlWriter>();
        services.AddSingleton<TeamYamlWriter>();
        services.AddSingleton<AgentYamlWriter>();
        services.AddSingleton<IReadOnlyList<GraphDefinition>>(sp =>
        {
            var loader = sp.GetRequiredService<FileBasedGraphLoader>();
            var sources = sp.GetRequiredService<AgentsOptions>().DefinitionSources;
            var definitions = sp.GetRequiredService<IReadOnlyList<AgentDefinition>>();
            var teams = sp.GetRequiredService<IReadOnlyList<TeamDefinition>>();
            return loader.LoadAllFromSources(
                sources,
                definitions.Select(definition => definition.Name).ToArray(),
                teams.Select(team => team.Name).ToArray(),
                sp.GetService<ILogger<DefinitionSourceResolver>>());
        });

        services.AddSingleton<AgentRegistry>(sp =>
        {
            var workflowLoader = sp.GetRequiredService<FileBasedWorkflowLoader>();
            var factory = sp.GetRequiredService<LlmAgentFactory>();
            var modelStore = sp.GetRequiredService<ILlmModelStore>();
            var logger = sp.GetRequiredService<ILogger<AgentRegistry>>();
            var approvalBridge = sp.GetRequiredService<HarnessApprovalBridge>();
            var sessionStore = sp.GetService<ISessionStore>();
            var approvalService = sp.GetService<IApprovalService>();
            var scriptRunner = sp.GetService<IWorkflowScriptRunner>();
            var defs = sp.GetRequiredService<IReadOnlyList<AgentDefinition>>();
            var toolCatalog = sp.GetRequiredService<AgentToolCatalog>();
            var costStore = sp.GetService<ICostStore>();
            var sources = sp.GetRequiredService<AgentsOptions>().DefinitionSources;
            var workflows = workflowLoader.LoadFromSources(sources, sp.GetService<ILogger<DefinitionSourceResolver>>());
            var deterministicE2eResponse = sp.GetRequiredService<IHostEnvironment>().EnvironmentName == "E2E"
                && string.Equals(configuration["E2E:DeterministicAgentResponse"], "true", StringComparison.OrdinalIgnoreCase);
            LogDefinitionSourceSummary(
                logger,
                sources,
                defs.Select(d => d.SourceLabel),
                sp.GetRequiredService<IReadOnlyList<TeamDefinition>>().Select(t => t.SourceLabel),
                sp.GetRequiredService<IReadOnlyList<GraphDefinition>>().Select(g => g.SourceLabel),
                workflows.Select(w => w.SourceLabel));
            LogToolPluginSummary(logger, toolCatalog.PluginLoadResults);
            return new AgentRegistry(
                defs, factory, modelStore, logger, approvalBridge, sessionStore, workflows,
                approvalService, scriptRunner, toolCatalog, costStore, deterministicE2eResponse);
        });
        services.AddSingleton<IAgentRegistry>(sp => sp.GetRequiredService<AgentRegistry>());
        services.AddSingleton<IWorkflowRegistry>(sp => sp.GetRequiredService<AgentRegistry>());
        services.AddSingleton<IAgentInvoker, AgentInvoker>();
        services.AddSingleton<IRunExecutor, AgentRunExecutor>();

        return services;
    }

    /// <summary>
    /// <c>Agents</c> セクションを手動バインドする(contracts/definition-source-config.md)。
    /// 未設定・空リストの場合は既存互換の単一標準パスへフォールバックする(後方互換)。
    /// </summary>
    private static AgentsOptions BuildAgentsOptions(IConfiguration configuration)
    {
        var options = new AgentsOptions();
        var section = configuration.GetSection(AgentsOptions.SectionName);

        foreach (var sourceSection in section.GetSection("DefinitionSources").GetChildren())
        {
            var label = sourceSection["Label"];
            var path = sourceSection["Path"];
            if (!string.IsNullOrWhiteSpace(label) && !string.IsNullOrWhiteSpace(path))
            {
                options.DefinitionSources.Add(new DefinitionSourceEntry { Label = label, Path = path });
            }
        }

        foreach (var dir in section.GetSection("ToolPluginDirectories").GetChildren())
        {
            if (!string.IsNullOrWhiteSpace(dir.Value))
            {
                options.ToolPluginDirectories.Add(dir.Value);
            }
        }

        foreach (var host in section.GetSection("ToolPlugins").GetSection("AllowedHosts").GetChildren())
        {
            if (!string.IsNullOrWhiteSpace(host.Value))
            {
                options.ToolPlugins.AllowedHosts.Add(host.Value);
            }
        }

        if (options.DefinitionSources.Count == 0)
        {
            // 後方互換フォールバック: 未設定時は既存の単一固定パスのみを "standard" ソースとして扱う(FR-001〜FR-003)。
            options.DefinitionSources.Add(new DefinitionSourceEntry
            {
                Label = "standard",
                Path = FileBasedAgentLoader.ResolveStandardSourceRoot(AppContext.BaseDirectory),
            });
        }

        return options;
    }

    /// <summary>
    /// 起動時にソースラベルごとの定義件数を1行のサマリーとしてログ出力する
    /// (FR-005・FR-006、data-model.md「解決済み定義」、specs/006-team-config-distribution User Story 3)。
    /// </summary>
    private static void LogDefinitionSourceSummary(
        ILogger logger,
        IReadOnlyList<DefinitionSourceEntry> sources,
        IEnumerable<string> agentSourceLabels,
        IEnumerable<string> teamSourceLabels,
        IEnumerable<string> graphSourceLabels,
        IEnumerable<string> workflowSourceLabels)
    {
        var allLabels = agentSourceLabels
            .Concat(teamSourceLabels)
            .Concat(graphSourceLabels)
            .Concat(workflowSourceLabels)
            .GroupBy(label => label, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);

        foreach (var source in sources)
        {
            var count = allLabels.GetValueOrDefault(source.Label, 0);
            logger.LogInformation(
                "definition source summary: label='{Label}' path='{Path}' definitionCount={Count}",
                source.Label, source.Path, count);
        }
    }

    /// <summary>
    /// 起動時にチーム固有ツールプラグインの読み込み結果を1行ずつログ出力する
    /// (contracts/tool-plugin-contract.md「読み込み結果の診断」、FR-006)。
    /// </summary>
    private static void LogToolPluginSummary(ILogger logger, IReadOnlyList<ToolPluginLoadResult> results)
    {
        foreach (var result in results)
        {
            if (result.LoadStatus == ToolPluginLoadStatus.Loaded)
            {
                logger.LogInformation(
                    "tool plugin summary: status=Loaded assembly='{Assembly}' provider='{Provider}' tools=[{Tools}]",
                    result.AssemblyPath, result.ProviderTypeName, string.Join(",", result.ToolNames));
            }
            else
            {
                logger.LogWarning(
                    "tool plugin summary: status=Failed assembly='{Assembly}' provider='{Provider}' reason='{Reason}'",
                    result.AssemblyPath, result.ProviderTypeName, result.FailureReason);
            }
        }
    }
}
