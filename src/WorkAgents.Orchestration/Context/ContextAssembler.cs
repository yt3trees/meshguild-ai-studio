using System.Text;
using WorkAgents.Core.Abstractions;
using WorkAgents.Core.Missions;
using WorkAgents.Orchestration.Teams;

namespace WorkAgents.Orchestration.Context;

public sealed record ContextAssemblyOptions
{
    public int MaxCharacters { get; init; } = 32_000;

    public int RecentMessageCount { get; init; } = 24;

    public string? RoleInstructions { get; init; }
}

public sealed record AssembledContext(
    string Text,
    bool UsedSummary,
    IReadOnlyList<Intervention> IncludedInterventions);

/// <summary>Builds deterministic turn context from persisted conversation state.</summary>
public sealed class ContextAssembler
{
    private readonly IMessageStore _messages;
    private readonly IInterventionStore? _interventions;
    private readonly MessageBus? _messagesBus;

    public ContextAssembler(IMessageStore messages, IInterventionStore? interventions = null, MessageBus? messagesBus = null)
    {
        ArgumentNullException.ThrowIfNull(messages);
        _messages = messages;
        _interventions = interventions;
        _messagesBus = messagesBus;
    }

    public async Task<AssembledContext> BuildAsync(
        string missionId,
        string instanceId,
        string goal,
        ContextAssemblyOptions? options = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(missionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceId);
        ArgumentNullException.ThrowIfNull(goal);
        options ??= new ContextAssemblyOptions();

        var messages = await _messages.ListAsync(
            missionId,
            sinceSeq: 0,
            limit: Math.Max(options.RecentMessageCount * 4, options.RecentMessageCount),
            ct: ct);
        var interventions = _interventions is null
            ? Array.Empty<Intervention>()
            : (await _interventions.ListUnappliedAsync(missionId, instanceId, ct)).ToArray();

        var builder = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(options.RoleInstructions))
        {
            builder.AppendLine(options.RoleInstructions.Trim());
        }
        builder.Append("Mission goal: ").AppendLine(goal.Trim());

        foreach (var intervention in interventions)
        {
            builder.Append("Human instruction: ").AppendLine(intervention.Body);
        }

        var usedSummary = false;
        var firstMessage = Math.Max(0, messages.Count - options.RecentMessageCount);
        if (firstMessage > 0)
        {
            usedSummary = true;
            builder.AppendLine("Earlier conversation was compressed; use the following recent messages as the authoritative tail.");
        }

        for (var i = firstMessage; i < messages.Count; i++)
        {
            var message = messages[i];
            builder.Append('[').Append(message.Seq).Append("] ")
                .Append(message.SenderKind).Append(' ')
                .Append(message.SenderInstanceId ?? "system")
                .Append(" -> ").Append(message.RecipientInstanceId ?? "all")
                .Append(" (").Append(message.Kind).Append("): ")
                .AppendLine(message.Body);
        }

        var text = builder.ToString();
        if (text.Length <= options.MaxCharacters)
        {
            return new AssembledContext(text, usedSummary, interventions);
        }

        usedSummary = true;
        var suffixLength = Math.Max(0, options.MaxCharacters - Math.Min(1_024, options.MaxCharacters / 4));
        var suffix = text.Length > suffixLength ? text[^suffixLength..] : text;
        var compact = "Earlier context omitted due to the input limit.\n" + suffix;
        if (_messagesBus is not null)
        {
            await _messagesBus.SendAsync(
                missionId,
                MessageSenderKind.System,
                MessageKind.SystemNote,
                "Conversation context was compressed because the input limit was reached.",
                ct: ct);
        }
        return new AssembledContext(compact, usedSummary, interventions);
    }

    public async Task MarkAppliedAsync(
        AssembledContext context,
        string appliedToMessageId,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(appliedToMessageId);
        if (_interventions is null)
        {
            return;
        }
        foreach (var intervention in context.IncludedInterventions)
        {
            await _interventions.MarkAppliedAsync(intervention.InterventionId, appliedToMessageId, ct);
        }
    }
}
