using Microsoft.Extensions.Logging;
using WorkAgents.Core.Abstractions;
using WorkAgents.Core.Missions;

namespace WorkAgents.Orchestration.Teams;

/// <summary>Persisted message notification raised after a message has been committed.</summary>
public sealed record MessagePublished(Message Message);

/// <summary>
/// The single write path for mission conversation messages. Sequence allocation remains
/// transactional in <see cref="IMessageStore"/>; this class owns redaction and fan-out.
/// </summary>
public sealed class MessageBus
{
    private readonly IMessageStore _messageStore;
    private readonly ISecretRedactor? _redactor;
    private readonly ILogger<MessageBus>? _logger;

    public MessageBus(
        IMessageStore messageStore,
        ISecretRedactor? redactor = null,
        ILogger<MessageBus>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(messageStore);
        _messageStore = messageStore;
        _redactor = redactor;
        _logger = logger;
    }

    public event Func<MessagePublished, Task>? Published;

    public async Task<Message> PublishAsync(Message message, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        var redacted = _redactor is null
            ? message
            : message with
            {
                Body = await _redactor.RedactAsync(message.Body, ct),
                InputRefs = message.InputRefs is null
                    ? null
                    : await _redactor.RedactAsync(message.InputRefs, ct),
            };

        var persisted = await _messageStore.AppendAsync(redacted, ct);
        _logger?.LogDebug(
            "mission message appended mission={MissionId} seq={Seq} kind={Kind}",
            persisted.MissionId,
            persisted.Seq,
            persisted.Kind);

        var handlers = Published;
        if (handlers is not null)
        {
            foreach (var handler in handlers.GetInvocationList().Cast<Func<MessagePublished, Task>>())
            {
                try
                {
                    await handler(new MessagePublished(persisted));
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "mission message subscriber failed for {MessageId}", persisted.MessageId);
                }
            }
        }

        return persisted;
    }

    public Task<Message> SendAsync(
        string missionId,
        MessageSenderKind senderKind,
        MessageKind kind,
        string body,
        string? senderInstanceId = null,
        string? recipientInstanceId = null,
        string? threadKey = null,
        string? inReplyTo = null,
        int delegationDepth = 0,
        string? nodeRunId = null,
        string? iterationId = null,
        string? inputRefs = null,
        string? costRecordId = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(missionId);
        ArgumentNullException.ThrowIfNull(body);

        return PublishAsync(new Message
        {
            MessageId = Guid.NewGuid().ToString("N"),
            MissionId = missionId,
            Seq = 0,
            ThreadKey = string.IsNullOrWhiteSpace(threadKey) ? "main" : threadKey,
            SenderKind = senderKind,
            SenderInstanceId = senderInstanceId,
            RecipientInstanceId = recipientInstanceId,
            Kind = kind,
            Body = body,
            InReplyTo = inReplyTo,
            DelegationDepth = delegationDepth,
            NodeRunId = nodeRunId,
            IterationId = iterationId,
            InputRefs = inputRefs,
            CostRecordId = costRecordId,
        }, ct);
    }
}
