using System.Collections.Concurrent;
using WorkAgents.Core.Missions;
using WorkAgents.Core.Teams;

namespace WorkAgents.Orchestration.Teams;

public sealed record ConversationDecision(bool Allowed, string Code, string Reason)
{
    public static ConversationDecision Allow() => new(true, "ok", "allowed");

    public static ConversationDecision Reject(string code, string reason) => new(false, code, reason);
}

/// <summary>Enforces team channel permissions and deterministic no-progress limits.</summary>
public sealed class ConversationPolicy
{
    private readonly TeamDefinition _team;
    private readonly ConcurrentDictionary<string, int> _pairRoundTrips = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, int> _pairWithoutProgress = new(StringComparer.Ordinal);

    public ConversationPolicy(TeamDefinition team)
    {
        ArgumentNullException.ThrowIfNull(team);
        _team = team;
    }

    public TeamDefinition Team => _team;

    public ConversationDecision Check(
        string from,
        string to,
        MessageKind kind,
        int delegationDepth = 0)
    {
        if (string.IsNullOrWhiteSpace(from) || string.IsNullOrWhiteSpace(to))
        {
            return ConversationDecision.Reject("invalid_participant", "both participants are required");
        }

        if (kind == MessageKind.Delegate && delegationDepth > _team.Limits.MaxDelegationDepth)
        {
            return ConversationDecision.Reject("delegation_depth_exceeded", "delegation depth limit exceeded");
        }

        if (string.Equals(from, to, StringComparison.OrdinalIgnoreCase))
        {
            return ConversationDecision.Reject("self_message", "an agent cannot message itself");
        }

        var isTeamParticipant = string.Equals(_team.Orchestrator.Agent, from, StringComparison.OrdinalIgnoreCase)
            || string.Equals(_team.Orchestrator.Agent, to, StringComparison.OrdinalIgnoreCase)
            || _team.Members.Any(member => string.Equals(member.Agent, from, StringComparison.OrdinalIgnoreCase))
            || _team.Members.Any(member => string.Equals(member.Agent, to, StringComparison.OrdinalIgnoreCase));
        if (!isTeamParticipant)
        {
            return ConversationDecision.Reject("unknown_member", "participant is not in the team");
        }

        var allowed = _team.ChannelsDefault == ChannelDefault.Direct
            || IsViaOrchestrator(from, to)
            || _team.ChannelsAllow.Any(rule =>
                string.Equals(rule.From, from, StringComparison.OrdinalIgnoreCase)
                && string.Equals(rule.To, to, StringComparison.OrdinalIgnoreCase)
                && rule.Kinds.Contains(kind));

        if (!allowed)
        {
            return ConversationDecision.Reject("channel_not_allowed", "the team channel does not allow this recipient");
        }

        return ConversationDecision.Allow();
    }

    public bool CanSend(string from, string to, MessageKind kind, int delegationDepth = 0)
        => Check(from, to, kind, delegationDepth).Allowed;

    public ConversationDecision CheckDelegation(string from, string to, int delegationDepth)
        => Check(from, to, MessageKind.Delegate, delegationDepth);

    public bool RecordRoundTrip(string from, string to, bool madeProgress)
    {
        var key = PairKey(from, to);
        var trips = _pairRoundTrips.AddOrUpdate(key, 1, static (_, current) => current + 1);
        if (madeProgress)
        {
            _pairWithoutProgress.TryRemove(key, out _);
        }
        else
        {
            _pairWithoutProgress.AddOrUpdate(key, 1, static (_, current) => current + 1);
        }

        return trips <= _team.Limits.NoProgressRoundTrips
            && (!_pairWithoutProgress.TryGetValue(key, out var stalled) || stalled <= _team.Limits.NoProgressRoundTrips);
    }

    public bool IsNoProgress(string from, string to)
    {
        var key = PairKey(from, to);
        return _pairWithoutProgress.TryGetValue(key, out var stalled)
            && stalled > _team.Limits.NoProgressRoundTrips;
    }

    public int RoundTrips(string from, string to)
        => _pairRoundTrips.TryGetValue(PairKey(from, to), out var count) ? count : 0;

    private bool IsViaOrchestrator(string from, string to)
        => string.Equals(from, _team.Orchestrator.Agent, StringComparison.OrdinalIgnoreCase)
            || string.Equals(to, _team.Orchestrator.Agent, StringComparison.OrdinalIgnoreCase);

    private static string PairKey(string from, string to) => $"{from}\u001f{to}";
}
