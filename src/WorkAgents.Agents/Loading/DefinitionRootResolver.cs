namespace WorkAgents.Agents.Loading;

/// <summary>
/// 標準定義ソースのルートを解決する。
/// 開発時は各プロセスの出力フォルダー、配布時はHost/Webの兄弟にある
/// <c>definitions/</c> フォルダーを優先する。
/// </summary>
internal static class DefinitionRootResolver
{
    private static readonly string[] DefinitionDirectories =
    [
        "agents",
        "skills",
        "teams",
        "graphs",
        "workflows",
    ];

    public static string ResolveSourceRoot(string baseDir)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseDir);

        var fullBaseDir = Path.GetFullPath(baseDir);
        if (ContainsDefinitionDirectory(fullBaseDir))
        {
            return fullBaseDir;
        }

        var commonRoot = Path.GetFullPath(Path.Combine(fullBaseDir, "..", "definitions"));
        if (ContainsDefinitionDirectory(commonRoot))
        {
            return commonRoot;
        }

        return ResolveDevelopmentSourceRoot();
    }

    public static string ResolveDirectory(string baseDir, string directoryName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directoryName);
        return Path.Combine(ResolveSourceRoot(baseDir), directoryName);
    }

    private static bool ContainsDefinitionDirectory(string root)
        => DefinitionDirectories.Any(name => Directory.Exists(Path.Combine(root, name)));

    private static string ResolveDevelopmentSourceRoot()
    {
        var agentsRoot = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "agents");
        return Path.GetFullPath(Path.Combine(agentsRoot, ".."));
    }
}
