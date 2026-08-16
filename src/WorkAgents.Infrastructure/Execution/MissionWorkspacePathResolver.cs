namespace WorkAgents.Infrastructure.Execution;

public sealed class MissionWorkspacePathResolver
{
    private readonly string _workspaceRoot;

    public MissionWorkspacePathResolver(string workspaceRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        _workspaceRoot = Path.GetFullPath(workspaceRoot);
    }

    public string ResolvePath(string missionId)
    {
        ValidateMissionId(missionId);
        var root = EnsureTrailingSeparator(_workspaceRoot);
        var path = Path.GetFullPath(Path.Combine(_workspaceRoot, "missions", missionId, "work"));
        if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Mission workspace escaped the configured workspace root.", nameof(missionId));
        }

        return path;
    }

    public string ResolveWorkspaceKey(string missionId)
    {
        ValidateMissionId(missionId);
        return $"missions/{missionId}/work";
    }

    private static void ValidateMissionId(string missionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(missionId);
        if (missionId is "." or ".."
            || missionId.Contains(Path.DirectorySeparatorChar)
            || missionId.Contains(Path.AltDirectorySeparatorChar)
            || Path.IsPathRooted(missionId)
            || missionId.Contains(':'))
        {
            throw new ArgumentException("Mission ID is not a safe path segment.", nameof(missionId));
        }
    }

    private static string EnsureTrailingSeparator(string path)
        => path.EndsWith(Path.DirectorySeparatorChar) || path.EndsWith(Path.AltDirectorySeparatorChar)
            ? path
            : path + Path.DirectorySeparatorChar;
}
