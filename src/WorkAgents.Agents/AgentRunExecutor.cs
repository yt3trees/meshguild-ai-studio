using WorkAgents.Core;
using WorkAgents.Core.Abstractions;

namespace WorkAgents.Agents;

/// <summary>RunBackgroundServiceからAgentRegistryへ接続する実行アダプター。</summary>
public sealed class AgentRunExecutor : IRunExecutor
{
    private readonly IAgentRegistry _registry;
    private readonly ProfileOptions _profile;

    public AgentRunExecutor(IAgentRegistry registry, ProfileOptions profile)
    {
        _registry = registry;
        _profile = profile;
    }

    public Task<string> ExecuteAsync(RunRecord run, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(run);

        var root = Path.GetFullPath(_profile.WorkspaceRoot);
        var workDirectory = Path.GetFullPath(Path.Combine(root, run.RunId));
        var rootWithSeparator = root.EndsWith(Path.DirectorySeparatorChar)
            ? root
            : root + Path.DirectorySeparatorChar;
        if (!workDirectory.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Run workspace escaped the configured workspace root.");
        }

        Directory.CreateDirectory(workDirectory);
        return _registry.RunAsync(run.AgentName, run.UserMessage, workDirectory, run.ThreadId, run.RunId, ct);
    }
}