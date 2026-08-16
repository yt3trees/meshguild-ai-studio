using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using WorkAgents.Agents.Loading;
using WorkAgents.Harness.Harness;

namespace WorkAgents.Agents.Tools;

/// <summary>起動時に構築されるAgent別の関数ツールカタログ。</summary>
public sealed class AgentToolCatalog
{
    private static readonly Regex ToolNamePattern = new(
        "^[a-z0-9_]+$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private Dictionary<string, IReadOnlyList<AgentToolRegistration>> _registrationsByAgent;

    public AgentToolCatalog(IServiceProvider services, IReadOnlyList<AgentDefinition> definitions)
        : this(definitions, CreateProviders(services, typeof(AgentToolCatalog).Assembly), services)
    {
    }

    /// <summary>
    /// 本体標準のツールに加え、<paramref name="toolPluginDirectories"/> 配下のDLLプラグインと
    /// スクリプトツール(JavaScript/Python、<c>*.tool.yaml</c>)もスキャンして登録する
    /// (FR-004、FR-010、contracts/tool-plugin-contract.md、contracts/script-tool-contract.md、
    /// specs/006-team-config-distribution)。
    /// </summary>
    public static AgentToolCatalog CreateWithPlugins(
        IServiceProvider services,
        IReadOnlyList<AgentDefinition> definitions,
        IReadOnlyList<string> toolPluginDirectories,
        ILogger<AgentToolCatalog>? logger = null,
        IEnumerable<IAgentToolProvider>? extraProviders = null)
    {
        var builtInProviders = CreateProviders(services, typeof(AgentToolCatalog).Assembly)
            .Concat(extraProviders ?? []);

        var (dllTriples, dllPreFailures) = ToolPluginLoader.Load(toolPluginDirectories, services, logger);

        var allowlist = services.GetService<IToolPluginHostAllowlist>() ?? new ToolPluginHostAllowlist([]);
        var (scriptTriples, scriptPreFailures) = ScriptToolLoader.Load(toolPluginDirectories, allowlist, logger);

        var pluginTriples = dllTriples.Concat(scriptTriples).ToArray();
        var preFailures = dllPreFailures.Concat(scriptPreFailures).ToArray();
        var pluginMetadata = pluginTriples.ToDictionary(
            triple => triple.Provider,
            triple => (triple.AssemblyPath, triple.ProviderType));
        var allProviders = builtInProviders.Concat(pluginTriples.Select(triple => triple.Provider));

        return new AgentToolCatalog(definitions, allProviders, services, pluginMetadata, preFailures, logger);
    }

    public AgentToolCatalog(
        IReadOnlyList<AgentDefinition> definitions,
        IEnumerable<IAgentToolProvider> providers)
        : this(definitions, providers, EmptyServiceProvider.Instance)
    {
    }

    private AgentToolCatalog(
        IReadOnlyList<AgentDefinition> definitions,
        IEnumerable<IAgentToolProvider> providers,
        IServiceProvider services)
        : this(definitions, providers, services, EmptyPluginMetadata, [], logger: null)
    {
    }

    private AgentToolCatalog(
        IReadOnlyList<AgentDefinition> definitions,
        IEnumerable<IAgentToolProvider> providers,
        IServiceProvider services,
        IReadOnlyDictionary<IAgentToolProvider, (string AssemblyPath, Type ProviderType)> pluginMetadata,
        IReadOnlyList<ToolPluginLoadResult> pluginPreFailures,
        ILogger? logger)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        ArgumentNullException.ThrowIfNull(providers);
        ArgumentNullException.ThrowIfNull(services);

        var knownAgents = definitions
            .Where(definition => !string.IsNullOrWhiteSpace(definition.Name))
            .GroupBy(definition => definition.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().Name, StringComparer.OrdinalIgnoreCase);
        var knownDefinitions = definitions
            .Where(definition => !string.IsNullOrWhiteSpace(definition.Name))
            .GroupBy(definition => definition.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var registrations = new Dictionary<string, List<AgentToolRegistration>>(StringComparer.OrdinalIgnoreCase);
        var toolNamesByAgent = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        var pluginResults = new List<ToolPluginLoadResult>(pluginPreFailures);

        // 標準/組み込みProviderを先に処理し、プラグインProviderは後段で処理する。
        // これによりプラグイン側の名称衝突は常に「後勝ち上書き」(FR-005)として扱える(処理順序に依存しない)。
        var orderedProviders = providers
            .OrderBy(provider => pluginMetadata.ContainsKey(provider!) ? 1 : 0)
            .ThenBy(provider => provider?.GetType().FullName, StringComparer.Ordinal);

        foreach (var provider in orderedProviders)
        {
            if (provider is null)
            {
                throw new InvalidOperationException("An agent tool provider collection contained a null provider.");
            }

            var isPlugin = pluginMetadata.TryGetValue(provider, out var pluginInfo);
            var providerType = provider.GetType();

            try
            {
                string providerAgentName;
                try
                {
                    providerAgentName = provider.AgentName;
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException(
                        $"Failed to resolve AgentName for agent tool provider '{providerType.FullName}'; AgentName='<unavailable>'.",
                        ex);
                }

                if (string.IsNullOrWhiteSpace(providerAgentName)
                    || !knownAgents.TryGetValue(providerAgentName, out var canonicalAgentName))
                {
                    throw new InvalidOperationException(
                        $"Agent tool provider '{providerType.FullName}' targets unknown AgentName '{providerAgentName}'.");
                }

                IReadOnlyList<AgentToolRegistration>? providerRegistrations;
                try
                {
                    providerRegistrations = provider.CreateTools(services);
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException(
                        $"Failed to create tools for provider '{providerType.FullName}' and AgentName '{providerAgentName}'.",
                        ex);
                }

                if (providerRegistrations is null)
                {
                    throw new InvalidOperationException(
                        $"Agent tool provider '{providerType.FullName}' returned null tools for AgentName '{providerAgentName}'.");
                }

                if (!registrations.TryGetValue(canonicalAgentName, out var agentRegistrations))
                {
                    agentRegistrations = [];
                    registrations[canonicalAgentName] = agentRegistrations;
                    toolNamesByAgent[canonicalAgentName] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                }

                foreach (var registration in providerRegistrations)
                {
                    ValidateRegistration(
                        registration,
                        providerType,
                        providerAgentName,
                        knownDefinitions[canonicalAgentName]);

                    if (!toolNamesByAgent[canonicalAgentName].Add(registration.Name))
                    {
                        if (!isPlugin)
                        {
                            throw new InvalidOperationException(
                                $"Duplicate agent tool name '{registration.Name}' for AgentName '{providerAgentName}' " +
                                $"from provider '{providerType.FullName}'.");
                        }

                        // チーム固有ツールプラグインは標準ツール/先行プラグインと同名の場合、後勝ちで上書きする(FR-005)。
                        logger?.LogWarning(
                            "tool plugin provider '{Provider}' overrides existing tool '{Tool}' for AgentName '{Agent}'",
                            providerType.FullName, registration.Name, providerAgentName);
                        agentRegistrations.RemoveAll(existing => string.Equals(existing.Name, registration.Name, StringComparison.OrdinalIgnoreCase));
                    }

                    agentRegistrations.Add(ApplyApprovalPolicy(registration, providerType, providerAgentName));
                }

                if (isPlugin)
                {
                    pluginResults.Add(new ToolPluginLoadResult
                    {
                        AssemblyPath = pluginInfo.AssemblyPath,
                        ProviderTypeName = providerType.FullName,
                        ToolNames = providerRegistrations.Select(r => r.Name).ToArray(),
                        LoadStatus = ToolPluginLoadStatus.Loaded,
                    });
                }
            }
            catch (Exception ex) when (isPlugin)
            {
                // プラグイン由来の失敗は当該プラグインだけをスキップし、カタログ全体の構築は継続する(FR-006)。
                logger?.LogError(ex, "failed to register tools from plugin provider '{Provider}'", providerType.FullName);
                pluginResults.Add(new ToolPluginLoadResult
                {
                    AssemblyPath = pluginInfo.AssemblyPath,
                    ProviderTypeName = providerType.FullName,
                    LoadStatus = ToolPluginLoadStatus.Failed,
                    FailureReason = ex.Message,
                });
            }
        }

        _registrationsByAgent = registrations.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyList<AgentToolRegistration>)pair.Value
                .OrderBy(registration => registration.Name, StringComparer.Ordinal)
                .ToArray(),
            StringComparer.OrdinalIgnoreCase);
        PluginLoadResults = pluginResults;
    }

    /// <summary>チーム固有ツールプラグインの読み込み結果(data-model.md「ツールプラグイン登録エントリ」)。</summary>
    public IReadOnlyList<ToolPluginLoadResult> PluginLoadResults { get; } = [];

    private static readonly IReadOnlyDictionary<IAgentToolProvider, (string AssemblyPath, Type ProviderType)> EmptyPluginMetadata =
        new Dictionary<IAgentToolProvider, (string, Type)>();

    public static AgentToolCatalog Empty { get; } = new(
        Array.Empty<AgentDefinition>(),
        Array.Empty<IAgentToolProvider>());

    public IReadOnlyList<AITool> GetTools(string agentName)
        => GetRegistrations(agentName)
            .Select(registration => registration.Tool)
            .ToArray();

    public IReadOnlyList<AgentToolRegistration> GetRegistrations(string agentName)
        => !string.IsNullOrWhiteSpace(agentName)
            && _registrationsByAgent.TryGetValue(agentName, out var registrations)
                ? registrations
                : Array.Empty<AgentToolRegistration>();

    /// <summary>Adds generated tools after the static provider scan has completed.</summary>
    public bool AddRegistration(string agentName, AgentToolRegistration registration)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentName);
        ValidateRegistration(registration, typeof(AgentToolCatalog), agentName, new AgentDefinition
        {
            Name = agentName,
            Instructions = string.Empty,
            FolderPath = string.Empty,
        });

        var current = GetRegistrations(agentName).ToList();
        if (current.Any(existing => string.Equals(existing.Name, registration.Name, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }
        current.Add(registration);
        _registrationsByAgent[agentName] = current
            .OrderBy(item => item.Name, StringComparer.Ordinal)
            .ToArray();
        return true;
    }

    private static void ValidateRegistration(
        AgentToolRegistration registration,
        Type providerType,
        string providerAgentName,
        AgentDefinition definition)
    {
        if (registration is null)
        {
            throw new InvalidOperationException(
                $"Agent tool provider '{providerType.FullName}' returned a null registration for AgentName '{providerAgentName}'.");
        }

        if (string.IsNullOrWhiteSpace(registration.Name))
        {
            throw new InvalidOperationException(
                $"Agent tool provider '{providerType.FullName}' returned an empty tool name for AgentName '{providerAgentName}'.");
        }

        if (!ToolNamePattern.IsMatch(registration.Name))
        {
            throw new InvalidOperationException(
                $"Agent tool provider '{providerType.FullName}' returned invalid tool name '{registration.Name}' " +
                $"for AgentName '{providerAgentName}'. Tool names must contain only lowercase letters, digits, and underscores.");
        }

        if (string.IsNullOrWhiteSpace(registration.Description))
        {
            throw new InvalidOperationException(
                $"Agent tool provider '{providerType.FullName}' returned an empty description for tool '{registration.Name}' " +
                $"and AgentName '{providerAgentName}'.");
        }

        if (!string.Equals(registration.Approval, "automatic", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(registration.Approval, "required", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Agent tool provider '{providerType.FullName}' returned unsupported approval '{registration.Approval}' " +
                $"for tool '{registration.Name}' and AgentName '{providerAgentName}'.");
        }

        if (registration.Tool is null || string.IsNullOrWhiteSpace(registration.Tool.Name))
        {
            throw new InvalidOperationException(
                $"Agent tool provider '{providerType.FullName}' returned a tool without a name for registration '{registration.Name}' " +
                $"and AgentName '{providerAgentName}'.");
        }

        if (!string.Equals(registration.Name, registration.Tool.Name, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Agent tool registration '{registration.Name}' does not match the AITool name '{registration.Tool.Name}' " +
                $"for provider '{providerType.FullName}' and AgentName '{providerAgentName}'.");
        }

        if (NeedsHarness(definition) && HarnessToolCatalog.AllNames.Contains(registration.Name))
        {
            throw new InvalidOperationException(
                $"Agent tool name '{registration.Name}' from provider '{providerType.FullName}' collides with a Harness built-in tool " +
                $"for AgentName '{providerAgentName}'.");
        }
    }

    private static bool NeedsHarness(AgentDefinition definition)
        => definition.HarnessShell
            || string.Equals(definition.HarnessFileStore, "workspace", StringComparison.OrdinalIgnoreCase)
            || definition.LocalSkillNames.Count > 0
            || definition.SharedSkillNames.Count > 0;

    private static AgentToolRegistration ApplyApprovalPolicy(
        AgentToolRegistration registration,
        Type providerType,
        string providerAgentName)
    {
        if (!string.Equals(registration.Approval, "required", StringComparison.OrdinalIgnoreCase))
        {
            return registration;
        }

        if (registration.Tool is not AIFunction function)
        {
            throw new InvalidOperationException(
                $"Agent tool '{registration.Name}' marked as required must be an AIFunction for provider " +
                $"'{providerType.FullName}' and AgentName '{providerAgentName}'.");
        }

        return registration with { Tool = new ApprovalRequiredAIFunction(function) };
    }

    private static IEnumerable<IAgentToolProvider> CreateProviders(
        IServiceProvider services,
        Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(services);

        var providerTypes = assembly.GetTypes()
            .Where(type => typeof(IAgentToolProvider).IsAssignableFrom(type))
            .Where(type => type is { IsAbstract: false, IsInterface: false })
            // ScriptToolProvider はマニフェストごとに ScriptToolLoader が手動で構築するため、
            // DIの自動解決を前提とするこのアセンブリ内スキャンからは除外する。
            .Where(type => type != typeof(ScriptToolProvider))
            .OrderBy(type => type.FullName, StringComparer.Ordinal);

        foreach (var providerType in providerTypes)
        {
            object provider;
            try
            {
                provider = ActivatorUtilities.CreateInstance(services, providerType);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"Failed to create agent tool provider '{providerType.FullName}'; AgentName='<unavailable>'.",
                    ex);
            }

            yield return (IAgentToolProvider)provider;
        }
    }

    private sealed class EmptyServiceProvider : IServiceProvider
    {
        public static EmptyServiceProvider Instance { get; } = new();

        public object? GetService(Type serviceType) => null;
    }
}
