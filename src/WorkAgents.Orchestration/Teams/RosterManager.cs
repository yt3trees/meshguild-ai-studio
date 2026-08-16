using WorkAgents.Core.Abstractions;
using WorkAgents.Core.Missions;
using WorkAgents.Core.Teams;

namespace WorkAgents.Orchestration.Teams;

public sealed record RosterChangeResult(bool Accepted, string Code, string? InstanceId = null)
{
    public static RosterChangeResult Reject(string code) => new(false, code);

    public static RosterChangeResult Accept(string? instanceId = null) => new(true, "ok", instanceId);
}

/// <summary>Applies runtime participant changes without changing team definition limits.</summary>
public sealed class RosterManager
{
    private readonly IAgentInstanceStore _instances;
    private readonly MessageBus _messages;

    public RosterManager(IAgentInstanceStore instances, MessageBus messages)
    {
        ArgumentNullException.ThrowIfNull(instances);
        ArgumentNullException.ThrowIfNull(messages);
        _instances = instances;
        _messages = messages;
    }

    public async Task<RosterChangeResult> AddParticipantAsync(
        string missionId,
        TeamDefinition team,
        string agentName,
        string reason,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(team);
        var member = team.Members.FirstOrDefault(m => string.Equals(m.Agent, agentName, StringComparison.OrdinalIgnoreCase));
        if (member is null)
        {
            return await RejectAsync(missionId, agentName, "not_in_team", reason, ct);
        }

        var existing = await _instances.ListByMissionAsync(missionId, ct);
        var active = existing.Count(IsActive);
        var agentActive = existing.Count(i => IsActive(i) && string.Equals(i.AgentName, agentName, StringComparison.OrdinalIgnoreCase));
        if (active >= team.Limits.MaxParallelInstances || agentActive >= member.MaxInstances)
        {
            return await RejectAsync(missionId, agentName, "instance_limit_reached", reason, ct);
        }

        var instance = new AgentInstance
        {
            InstanceId = Guid.NewGuid().ToString("N"),
            MissionId = missionId,
            AgentName = member.Agent,
            Role = AgentInstanceRole.Member,
            InstanceNo = existing.Count(i => string.Equals(i.AgentName, member.Agent, StringComparison.OrdinalIgnoreCase)) + 1,
            JoinReason = reason,
        };
        await _instances.CreateAsync(instance, ct);
        await _messages.SendAsync(
            missionId,
            MessageSenderKind.System,
            MessageKind.RosterChange,
            $"Participant added: {member.Agent}. Reason: {reason}",
            recipientInstanceId: instance.InstanceId,
            ct: ct);
        return RosterChangeResult.Accept(instance.InstanceId);
    }

    public async Task<RosterChangeResult> RemoveParticipantAsync(
        string missionId,
        TeamDefinition team,
        string instanceId,
        string reason,
        CancellationToken ct = default)
    {
        var instance = await _instances.GetAsync(instanceId, ct);
        if (instance is null || !string.Equals(instance.MissionId, missionId, StringComparison.Ordinal))
        {
            return RosterChangeResult.Reject("unknown_instance");
        }
        if (instance.Role == AgentInstanceRole.Orchestrator || instance.State is AgentInstanceState.Thinking or AgentInstanceState.ToolRunning)
        {
            return RosterChangeResult.Reject(instance.Role == AgentInstanceRole.Orchestrator ? "cannot_remove_orchestrator" : "instance_busy");
        }

        if (instance.State != AgentInstanceState.Stopped)
        {
            await _instances.SetStateAsync(instanceId, AgentInstanceState.Stopped, ct: ct);
        }
        await _instances.SetLeftAsync(instanceId, reason, ct);
        await _messages.SendAsync(
            missionId,
            MessageSenderKind.System,
            MessageKind.RosterChange,
            $"Participant removed: {instance.AgentName}. Reason: {reason}",
            recipientInstanceId: instanceId,
            ct: ct);
        return RosterChangeResult.Accept(instanceId);
    }

    public async Task<RosterChangeResult> ScaleAgentAsync(
        string missionId,
        TeamDefinition team,
        string agentName,
        int instances,
        string reason,
        CancellationToken ct = default)
    {
        if (instances < 0)
        {
            return RosterChangeResult.Reject("invalid_instance_count");
        }

        var member = team.Members.FirstOrDefault(m => string.Equals(m.Agent, agentName, StringComparison.OrdinalIgnoreCase));
        if (member is null)
        {
            return RosterChangeResult.Reject("not_in_team");
        }
        if (instances > member.MaxInstances)
        {
            return RosterChangeResult.Reject("instance_limit_reached");
        }

        var current = (await _instances.ListByMissionAsync(missionId, ct))
            .Where(i => string.Equals(i.AgentName, member.Agent, StringComparison.OrdinalIgnoreCase) && IsActive(i))
            .ToList();
        while (current.Count < instances)
        {
            var result = await AddParticipantAsync(missionId, team, member.Agent, reason, ct);
            if (!result.Accepted)
            {
                return result;
            }
            current.Add(await _instances.GetAsync(result.InstanceId!, ct) ?? throw new InvalidOperationException("Roster instance was not persisted."));
        }

        foreach (var extra in current.Skip(instances).ToArray())
        {
            var result = await RemoveParticipantAsync(missionId, team, extra.InstanceId, reason, ct);
            if (!result.Accepted)
            {
                return result;
            }
        }

        await _messages.SendAsync(
            missionId,
            MessageSenderKind.System,
            MessageKind.RosterChange,
            $"Participant scale changed: {member.Agent}={instances}. Reason: {reason}",
            ct: ct);
        return RosterChangeResult.Accept();
    }

    private Task<RosterChangeResult> RejectAsync(
        string missionId,
        string agentName,
        string code,
        string reason,
        CancellationToken ct)
        => _messages.SendAsync(
            missionId,
            MessageSenderKind.System,
            MessageKind.Rejected,
            $"Roster change rejected ({code}) for {agentName}: {reason}",
            ct: ct).ContinueWith(_ => RosterChangeResult.Reject(code), ct);

    private static bool IsActive(AgentInstance instance)
        => instance.State is not (AgentInstanceState.Completed or AgentInstanceState.Failed or AgentInstanceState.Stopped);
}
