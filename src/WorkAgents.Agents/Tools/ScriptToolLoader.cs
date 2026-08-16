using Microsoft.Extensions.Logging;

namespace WorkAgents.Agents.Tools;

/// <summary>
/// <c>ToolPluginDirectories</c> 配下の <c>*.tool.yaml</c>(スクリプトツールマニフェスト)を走査し、
/// <see cref="ScriptToolProvider"/> を生成する(contracts/script-tool-contract.md、FR-010〜FR-014)。
/// 個々のマニフェストの不正・スクリプト不在・allowlist不足は当該ツールだけをスキップし、他の読み込みは継続する。
/// </summary>
public static class ScriptToolLoader
{
    private const string ManifestExtension = ".tool.yaml";

    public static (
        IReadOnlyList<(IAgentToolProvider Provider, string AssemblyPath, Type ProviderType)> Providers,
        IReadOnlyList<ToolPluginLoadResult> PreFailures) Load(
        IReadOnlyList<string> pluginDirectories,
        IToolPluginHostAllowlist allowlist,
        ILogger? logger)
    {
        ArgumentNullException.ThrowIfNull(allowlist);

        var providers = new List<(IAgentToolProvider, string, Type)>();
        var preFailures = new List<ToolPluginLoadResult>();

        foreach (var dir in pluginDirectories)
        {
            if (!Directory.Exists(dir))
            {
                // ToolPluginLoader 側でも同じディレクトリを走査しており、そこで既にログ済みのため、ここでは静かにスキップする。
                continue;
            }

            foreach (var manifestPath in Directory.EnumerateFiles(dir, $"*{ManifestExtension}"))
            {
                LoadManifest(manifestPath, allowlist, logger, providers, preFailures);
            }
        }

        return (providers, preFailures);
    }

    private static void LoadManifest(
        string manifestPath,
        IToolPluginHostAllowlist allowlist,
        ILogger? logger,
        List<(IAgentToolProvider, string, Type)> providers,
        List<ToolPluginLoadResult> preFailures)
    {
        ScriptToolManifest manifest;
        try
        {
            var yaml = File.ReadAllText(manifestPath);
            manifest = ScriptToolManifestSerializer.Deserialize(yaml, manifestPath);
        }
        catch (ScriptToolManifestValidationException ex)
        {
            logger?.LogError(ex, "failed to parse script tool manifest: {Path}", manifestPath);
            preFailures.Add(new ToolPluginLoadResult
            {
                AssemblyPath = manifestPath,
                LoadStatus = ToolPluginLoadStatus.Failed,
                FailureReason = ex.Message,
            });
            return;
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "failed to read script tool manifest: {Path}", manifestPath);
            preFailures.Add(new ToolPluginLoadResult
            {
                AssemblyPath = manifestPath,
                LoadStatus = ToolPluginLoadStatus.Failed,
                FailureReason = ex.Message,
            });
            return;
        }

        var manifestDir = Path.GetDirectoryName(manifestPath) ?? "";
        var entryPointPath = Path.Combine(manifestDir, manifest.EntryPoint);
        if (!File.Exists(entryPointPath))
        {
            var reason = $"entryPoint not found: '{entryPointPath}'";
            logger?.LogError("script tool '{Name}' entryPoint not found: {EntryPoint} (manifest={Manifest})", manifest.Name, entryPointPath, manifestPath);
            preFailures.Add(new ToolPluginLoadResult
            {
                AssemblyPath = manifestPath,
                ProviderTypeName = manifest.Name,
                LoadStatus = ToolPluginLoadStatus.Failed,
                FailureReason = reason,
            });
            return;
        }

        var disallowedHosts = manifest.AllowedHosts.Where(host => !allowlist.IsAllowed(host)).ToArray();
        if (disallowedHosts.Length > 0)
        {
            var reason = $"allowedHosts not permitted by Agents:ToolPlugins:AllowedHosts: {string.Join(", ", disallowedHosts)}";
            logger?.LogError(
                "script tool '{Name}' declares hosts outside the global allowlist, skipping: {Hosts} (manifest={Manifest})",
                manifest.Name, string.Join(", ", disallowedHosts), manifestPath);
            preFailures.Add(new ToolPluginLoadResult
            {
                AssemblyPath = manifestPath,
                ProviderTypeName = manifest.Name,
                LoadStatus = ToolPluginLoadStatus.Failed,
                FailureReason = reason,
            });
            return;
        }

        var provider = new ScriptToolProvider(manifest, entryPointPath);
        providers.Add((provider, manifestPath, typeof(ScriptToolProvider)));
    }
}
