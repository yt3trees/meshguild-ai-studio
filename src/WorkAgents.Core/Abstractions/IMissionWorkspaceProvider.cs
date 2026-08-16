namespace WorkAgents.Core.Abstractions;

public interface IMissionWorkspaceProvider
{
    Task<string> PrepareAsync(string missionId, CancellationToken ct = default);

    string ResolvePath(string missionId);
}
