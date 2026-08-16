using WorkAgents.Core.Abstractions;
using WorkAgents.Core.Missions;

namespace WorkAgents.Infrastructure.Execution;

public sealed class MissionWorkspaceProvider : IMissionWorkspaceProvider
{
    private readonly MissionWorkspacePathResolver _paths;
    private readonly IMissionWorkspaceStore _store;
    private readonly TimeProvider _clock;

    public MissionWorkspaceProvider(
        MissionWorkspacePathResolver paths,
        IMissionWorkspaceStore store,
        TimeProvider? clock = null)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(store);
        _paths = paths;
        _store = store;
        _clock = clock ?? TimeProvider.System;
    }

    public string ResolvePath(string missionId)
        => _paths.ResolvePath(missionId);

    public async Task<string> PrepareAsync(string missionId, CancellationToken ct = default)
    {
        var path = _paths.ResolvePath(missionId);
        Directory.CreateDirectory(path);
        await _store.RecordPreparedAsync(new MissionWorkspaceRecord
        {
            MissionId = missionId,
            WorkspaceKey = _paths.ResolveWorkspaceKey(missionId),
            PreparedAtUtc = _clock.GetUtcNow(),
        }, ct);
        return path;
    }
}
