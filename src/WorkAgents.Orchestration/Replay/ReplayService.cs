using WorkAgents.Core.Abstractions;
using WorkAgents.Core.Missions;

namespace WorkAgents.Orchestration.Replay;

public sealed class ReplayService
{
    private readonly IMessageStore _messages;
    private readonly IMissionStore? _missions;

    public ReplayService(IMessageStore messages, IMissionStore? missions = null)
    {
        ArgumentNullException.ThrowIfNull(messages);
        _messages = messages;
        _missions = missions;
    }

    public Task<IReadOnlyList<Message>> ReplayAsync(string missionId, bool includeDiscarded = false, CancellationToken ct = default)
        => _messages.ListAsync(missionId, includeDiscarded: includeDiscarded, limit: 10_000, ct: ct);

    public async Task<IReadOnlyList<Mission>> QueryAsync(MissionQuery query, CancellationToken ct = default)
        => _missions is null ? Array.Empty<Mission>() : await _missions.ListAsync(query, ct);
}
