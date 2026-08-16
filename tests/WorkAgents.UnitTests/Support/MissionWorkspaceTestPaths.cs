namespace WorkAgents.UnitTests.Support;

public sealed class MissionWorkspaceTestPaths : IDisposable
{
    public MissionWorkspaceTestPaths()
    {
        Root = Path.Combine(Path.GetTempPath(), "work-agents-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
        DatabasePath = Path.Combine(Root, "state", "work-agents.db");
        Directory.CreateDirectory(Path.GetDirectoryName(DatabasePath)!);
    }

    public string Root { get; }

    public string DatabasePath { get; }

    public string MissionWorkspace(string missionId)
        => Path.Combine(Root, "missions", missionId, "work");

    public void Dispose()
    {
        if (Directory.Exists(Root))
        {
            Directory.Delete(Root, recursive: true);
        }
    }
}
