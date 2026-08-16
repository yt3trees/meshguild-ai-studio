namespace WorkAgents.Core.Abstractions;

public interface IMissionCancellationRegistry
{
    CancellationToken Register(string missionId);

    bool TryCancel(string missionId);

    void Remove(string missionId);
}
