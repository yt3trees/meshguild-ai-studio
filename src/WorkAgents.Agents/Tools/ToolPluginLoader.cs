using System.Reflection;
using System.Runtime.Loader;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace WorkAgents.Agents.Tools;

/// <summary>読み込み結果(<c>Loaded</c>/<c>Failed</c>)。</summary>
public enum ToolPluginLoadStatus
{
    Loaded,
    Failed,
}

/// <summary>
/// チーム固有ツールプラグイン1件分の読み込み結果(data-model.md「ツールプラグイン登録エントリ」)。
/// </summary>
public sealed record ToolPluginLoadResult
{
    public required string AssemblyPath { get; init; }

    public string? ProviderTypeName { get; init; }

    public IReadOnlyList<string> ToolNames { get; init; } = [];

    public required ToolPluginLoadStatus LoadStatus { get; init; }

    public string? FailureReason { get; init; }
}

/// <summary>
/// アセンブリを分離ロードコンテキストで読み込む(contracts/tool-plugin-contract.md)。
/// 本体の依存アセンブリと同名・異バージョンの依存関係を含んでいても本体側の解決に影響しない。
/// </summary>
internal sealed class ToolPluginLoadContext : AssemblyLoadContext
{
    private readonly AssemblyDependencyResolver _resolver;

    public ToolPluginLoadContext(string pluginAssemblyPath)
        : base(name: $"ToolPlugin:{Path.GetFileName(pluginAssemblyPath)}", isCollectible: false)
    {
        _resolver = new AssemblyDependencyResolver(pluginAssemblyPath);
    }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        var path = _resolver.ResolveAssemblyToPath(assemblyName);
        return path is null ? null : LoadFromAssemblyPath(path);
    }
}

/// <summary>
/// <c>Agents:ToolPluginDirectories</c>(<see cref="WorkAgents.Agents.Configuration.AgentsOptions"/>)配下の
/// アセンブリ(DLL)をスキャンし、
/// <see cref="IAgentToolProvider"/> 実装型のインスタンスを生成する(contracts/tool-plugin-contract.md、FR-004・FR-006)。
/// 個々のアセンブリ・型の読み込み失敗は当該分だけスキップし、他プラグインの読み込みは継続する。
/// </summary>
public static class ToolPluginLoader
{
    public static (
        IReadOnlyList<(IAgentToolProvider Provider, string AssemblyPath, Type ProviderType)> Providers,
        IReadOnlyList<ToolPluginLoadResult> PreFailures) Load(
        IReadOnlyList<string> pluginDirectories,
        IServiceProvider services,
        ILogger? logger)
    {
        var providers = new List<(IAgentToolProvider, string, Type)>();
        var preFailures = new List<ToolPluginLoadResult>();

        foreach (var dir in pluginDirectories)
        {
            if (!Directory.Exists(dir))
            {
                logger?.LogWarning("tool plugin directory not found, skipping: {Dir}", dir);
                continue;
            }

            foreach (var dllPath in Directory.EnumerateFiles(dir, "*.dll"))
            {
                LoadAssembly(dllPath, services, logger, providers, preFailures);
            }
        }

        return (providers, preFailures);
    }

    private static void LoadAssembly(
        string dllPath,
        IServiceProvider services,
        ILogger? logger,
        List<(IAgentToolProvider, string, Type)> providers,
        List<ToolPluginLoadResult> preFailures)
    {
        Assembly assembly;
        try
        {
            var context = new ToolPluginLoadContext(dllPath);
            // ファイルストリーム経由で読み込み、DLLファイルをロックしない(実行中のプラグイン更新を妨げない)。
            using var stream = new MemoryStream(File.ReadAllBytes(dllPath));
            assembly = context.LoadFromStream(stream);
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "failed to load tool plugin assembly: {Path}", dllPath);
            preFailures.Add(new ToolPluginLoadResult
            {
                AssemblyPath = dllPath,
                LoadStatus = ToolPluginLoadStatus.Failed,
                FailureReason = ex.Message,
            });
            return;
        }

        Type[] providerTypes;
        try
        {
            providerTypes = assembly.GetTypes()
                .Where(type => typeof(IAgentToolProvider).IsAssignableFrom(type))
                .Where(type => type is { IsAbstract: false, IsInterface: false })
                .OrderBy(type => type.FullName, StringComparer.Ordinal)
                .ToArray();
        }
        catch (ReflectionTypeLoadException ex)
        {
            logger?.LogError(ex, "failed to reflect types in tool plugin assembly: {Path}", dllPath);
            preFailures.Add(new ToolPluginLoadResult
            {
                AssemblyPath = dllPath,
                LoadStatus = ToolPluginLoadStatus.Failed,
                FailureReason = ex.Message,
            });
            return;
        }

        if (providerTypes.Length == 0)
        {
            logger?.LogWarning("tool plugin assembly has no IAgentToolProvider implementation, skipping: {Path}", dllPath);
            preFailures.Add(new ToolPluginLoadResult
            {
                AssemblyPath = dllPath,
                LoadStatus = ToolPluginLoadStatus.Failed,
                FailureReason = "no IAgentToolProvider implementation found",
            });
            return;
        }

        foreach (var providerType in providerTypes)
        {
            try
            {
                var provider = (IAgentToolProvider)ActivatorUtilities.CreateInstance(services, providerType);
                providers.Add((provider, dllPath, providerType));
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "failed to instantiate tool plugin provider '{Provider}' from {Path}", providerType.FullName, dllPath);
                preFailures.Add(new ToolPluginLoadResult
                {
                    AssemblyPath = dllPath,
                    ProviderTypeName = providerType.FullName,
                    LoadStatus = ToolPluginLoadStatus.Failed,
                    FailureReason = ex.Message,
                });
            }
        }
    }
}
