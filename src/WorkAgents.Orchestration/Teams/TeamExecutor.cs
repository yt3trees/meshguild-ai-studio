using System.Text.Json;
using Microsoft.Extensions.Logging;
using WorkAgents.Core.Abstractions;
using WorkAgents.Core.Missions;
using WorkAgents.Core.Teams;
using WorkAgents.Orchestration.Context;
using WorkAgents.Orchestration.Budgets;

namespace WorkAgents.Orchestration.Teams;

public sealed record TeamExecutionRequest
{
    public required string MissionId { get; init; }

    public required string Goal { get; init; }

    public required TeamDefinition Team { get; init; }

    public string? WorkingDirectory { get; init; }

    public int MaxTurns { get; init; } = 24;
}

public sealed record TeamExecutionResult(
    MissionOutcome Outcome,
    MissionStopReason? StopReason,
    IReadOnlyList<Message> Messages,
    IReadOnlyList<AgentInstance> Instances);

/// <summary>
/// Executes a team using one deterministic invoker turn at a time. Tool calls are
/// represented as conversation messages, so replay and control share the same path.
/// </summary>
public sealed class TeamExecutor
{
    private readonly IAgentInvoker _invoker;
    private readonly MessageBus _messages;
    private readonly IAgentInstanceStore? _instances;
    private readonly ContextAssembler? _context;
    private readonly ILogger<TeamExecutor>? _logger;
    private readonly CostAttribution? _costs;
    private readonly IAgentStreamSink? _streams;

    public event Func<AgentInstance, Task>? StateChanged;

    public TeamExecutor(
        IAgentInvoker invoker,
        MessageBus messages,
        IAgentInstanceStore? instances = null,
        ContextAssembler? context = null,
        ILogger<TeamExecutor>? logger = null,
        CostAttribution? costs = null,
        IAgentStreamSink? streams = null)
    {
        ArgumentNullException.ThrowIfNull(invoker);
        ArgumentNullException.ThrowIfNull(messages);
        _invoker = invoker;
        _messages = messages;
        _instances = instances;
        _context = context;
        _logger = logger;
        _costs = costs;
        _streams = streams is null or NullAgentStreamSink ? null : streams;
    }

    public async Task<TeamExecutionResult> ExecuteAsync(
        TeamExecutionRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var policy = new ConversationPolicy(request.Team);
        var waitGraph = new WaitGraph();
        var instances = await CreateRosterAsync(request, ct);
        var messages = new List<Message>();
        var orchestrator = instances.Single(instance => instance.Role == AgentInstanceRole.Orchestrator);
        var members = instances.Where(instance => instance.Role == AgentInstanceRole.Member).ToArray();
        var turns = 0;
        var failed = false;

        await SetStateAsync(orchestrator, AgentInstanceState.Thinking, ct);
        try
        {
            var orchestratorContext = _context is null
                ? request.Goal
                : (await _context.BuildAsync(
                    request.MissionId,
                    orchestrator.InstanceId,
                    request.Goal,
                    new ContextAssemblyOptions { RoleInstructions = "Human instructions take precedence over the current team plan." },
                    ct)).Text;
            var result = await InvokeAsync(orchestrator, orchestratorContext, request, ct);
            var orchestratorCostId = await RecordCostAsync(orchestrator, result, request, ct);
            turns++;
            messages.Add(await PublishAsync(request, orchestrator, MessageKind.Share, result.Utterance, costRecordId: orchestratorCostId, ct: ct));

            var delegations = result.ToolCalls
                .Where(call => string.Equals(call.ToolName, "delegate_task", StringComparison.OrdinalIgnoreCase))
                .Select(call => ParseCall(call.ArgsSummary))
                .ToList();
            if (delegations.Count == 0 && members.Length > 0)
            {
                delegations.Add(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["agent"] = members[0].AgentName,
                    ["instruction"] = request.Goal,
                });
            }

            foreach (var delegation in delegations)
            {
                if (turns >= request.MaxTurns)
                {
                    break;
                }

                var agentName = Value(delegation, "agent");
                var member = members.FirstOrDefault(instance => string.Equals(instance.AgentName, agentName, StringComparison.OrdinalIgnoreCase));
                var depth = 1;
                var decision = member is null
                    ? ConversationDecision.Reject("unknown_member", "delegation target is not in the team")
                    : policy.Check(orchestrator.AgentName, member.AgentName, MessageKind.Delegate, depth);
                if (!decision.Allowed)
                {
                    messages.Add(await PublishAsync(
                        request,
                        orchestrator,
                        MessageKind.Rejected,
                        $"delegate_task rejected ({decision.Code}): {decision.Reason}",
                        recipient: member?.InstanceId,
                        depth: depth,
                        ct: ct));
                    continue;
                }

                messages.Add(await PublishAsync(
                    request,
                    orchestrator,
                    MessageKind.Delegate,
                    Value(delegation, "instruction") ?? request.Goal,
                    recipient: member!.InstanceId,
                    depth: depth,
                    ct: ct));
                await SetStateAsync(member, AgentInstanceState.Thinking, ct);

                try
                {
                    AssembledContext? assembledContext = null;
                    var memberContext = _context is null
                        ? Value(delegation, "instruction") ?? request.Goal
                        : (assembledContext = await _context.BuildAsync(
                            request.MissionId,
                            member.InstanceId,
                            Value(delegation, "instruction") ?? request.Goal,
                            new ContextAssemblyOptions { RoleInstructions = "You are a delegated team member." },
                            ct)).Text;
                    var memberResult = await InvokeAsync(member, memberContext, request, ct);
                    var memberCostId = await RecordCostAsync(member, memberResult, request, ct);
                    turns++;
                    var reportMessage = await PublishAsync(
                        request,
                        member,
                        MessageKind.Report,
                        memberResult.Utterance,
                        recipient: orchestrator.InstanceId,
                        depth: depth,
                        costRecordId: memberCostId,
                        ct: ct);
                    messages.Add(reportMessage);
                    if (assembledContext is not null)
                    {
                        await _context!.MarkAppliedAsync(assembledContext, reportMessage.MessageId, ct);
                    }

                    foreach (var call in memberResult.ToolCalls)
                    {
                        if (turns >= request.MaxTurns)
                        {
                            break;
                        }

                        if (string.Equals(call.ToolName, "ask_agent", StringComparison.OrdinalIgnoreCase))
                        {
                            var ask = ParseCall(call.ArgsSummary);
                            var target = members.FirstOrDefault(instance =>
                                string.Equals(instance.AgentName, Value(ask, "agent"), StringComparison.OrdinalIgnoreCase));
                            var askDecision = target is null
                                ? ConversationDecision.Reject("unknown_member", "question target is not in the team")
                                : policy.Check(member.AgentName, target.AgentName, MessageKind.Question, depth);
                            if (!askDecision.Allowed)
                            {
                                messages.Add(await PublishAsync(request, member, MessageKind.Rejected,
                                    $"ask_agent rejected ({askDecision.Code}): {askDecision.Reason}", target?.InstanceId, depth: depth, ct: ct));
                                continue;
                            }

                            var wait = waitGraph.Register(member.InstanceId, target!.InstanceId);
                            if (!wait.Accepted)
                            {
                                messages.Add(await PublishAsync(request, member, MessageKind.Rejected,
                                    $"ask_agent rejected (deadlock_detected): {string.Join(" -> ", wait.Cycle)}", target.InstanceId, depth: depth, ct: ct));
                                continue;
                            }

                            await SetStateAsync(member, AgentInstanceState.AwaitingReply, ct, target.InstanceId);
                            var question = await PublishAsync(request, member, MessageKind.Question,
                                Value(ask, "question") ?? "Please provide your finding.", target.InstanceId, depth: depth, ct: ct);
                            await SetStateAsync(target, AgentInstanceState.Thinking, ct);
                            var answer = await InvokeAsync(target, question.Body, request, ct);
                            var answerCostId = await RecordCostAsync(target, answer, request, ct);
                            turns++;
                            messages.Add(await PublishAsync(request, target, MessageKind.Answer,
                                answer.Utterance, member.InstanceId, question.MessageId, depth, answerCostId, ct));
                            policy.RecordRoundTrip(member.AgentName, target.AgentName, madeProgress: !string.IsNullOrWhiteSpace(answer.Utterance));
                            waitGraph.Remove(member.InstanceId);
                            await SetStateAsync(member, AgentInstanceState.Thinking, ct);
                            await SetStateAsync(target, AgentInstanceState.Idle, ct);
                        }
                    }

                    await SetStateAsync(member, AgentInstanceState.Completed, ct);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    failed = true;
                    _logger?.LogWarning(ex, "team member turn failed agent={AgentName}", member.AgentName);
                    messages.Add(await PublishAsync(request, member, MessageKind.Report,
                        "The delegated task failed; the orchestrator must choose a recovery action.",
                        recipient: orchestrator.InstanceId, depth: depth, ct: ct));
                    await SetStateAsync(member, AgentInstanceState.Failed, ct);
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            failed = true;
            _logger?.LogWarning(ex, "team orchestration failed mission={MissionId}", request.MissionId);
            messages.Add(await PublishAsync(request, orchestrator, MessageKind.SystemNote,
                "The team orchestrator failed before the mission could converge.", ct: ct));
            await SetStateAsync(orchestrator, AgentInstanceState.Failed, ct);
        }

        if (!failed)
        {
            await SetStateAsync(orchestrator, AgentInstanceState.Completed, ct);
        }

        return new TeamExecutionResult(
            failed ? MissionOutcome.Failed : MissionOutcome.Succeeded,
            failed ? MissionStopReason.OrchestratorFailure : MissionStopReason.StopConditionMet,
            messages,
            instances);
    }

    private async Task<IReadOnlyList<AgentInstance>> CreateRosterAsync(TeamExecutionRequest request, CancellationToken ct)
    {
        var instances = new List<AgentInstance>
        {
            new()
            {
                InstanceId = Guid.NewGuid().ToString("N"),
                MissionId = request.MissionId,
                AgentName = request.Team.Orchestrator.Agent,
                Role = AgentInstanceRole.Orchestrator,
                InstanceNo = 1,
            },
        };
        instances.AddRange(request.Team.Members.Select((member, index) => new AgentInstance
        {
            InstanceId = Guid.NewGuid().ToString("N"),
            MissionId = request.MissionId,
            AgentName = member.Agent,
            Role = AgentInstanceRole.Member,
            InstanceNo = index + 1,
        }));

        if (_instances is not null)
        {
            foreach (var instance in instances)
            {
                await _instances.CreateAsync(instance, ct);
            }
        }
        return instances;
    }

    private async Task<AgentInvocationResult> InvokeAsync(
        AgentInstance instance,
        string context,
        TeamExecutionRequest request,
        CancellationToken ct)
    {
        var invocation = new AgentInvocation
        {
            AgentName = instance.AgentName,
            Context = context,
            WorkingDirectory = request.WorkingDirectory,
            MissionId = request.MissionId,
            ThreadId = $"mission:{request.MissionId}:{instance.InstanceId}",
        };

        if (_streams is null)
        {
            return await _invoker.InvokeAsync(invocation, ct);
        }

        return await InvokeStreamingAsync(instance, invocation, request, ct);
    }

    /// <summary>
    /// 途中経過を <see cref="IAgentStreamSink"/> へ流しながら 1 ターンを実行する。
    /// 配信は最善努力であり、確定した発言は呼び出し側が従来どおり <see cref="MessageBus"/> へ書き込む。
    /// </summary>
    private async Task<AgentInvocationResult> InvokeStreamingAsync(
        AgentInstance instance,
        AgentInvocation invocation,
        TeamExecutionRequest request,
        CancellationToken ct)
    {
        var sink = _streams!;
        var streamId = $"{request.MissionId}:{instance.InstanceId}:{Guid.NewGuid():N}";
        AgentInvocationResult? completed = null;
        var started = false;
        var interrupted = false;
        var seq = 0L;

        try
        {
            await foreach (var update in _invoker.InvokeStreamingAsync(invocation, ct))
            {
                switch (update)
                {
                    case AgentTextDeltaUpdate text:
                        if (!started)
                        {
                            started = true;
                            await sink.StartedAsync(new AgentStreamStarted
                            {
                                MissionId = request.MissionId,
                                StreamId = streamId,
                                InstanceId = instance.InstanceId,
                                AgentName = instance.AgentName,
                            }, ct);
                        }

                        await sink.DeltaAsync(new AgentStreamDelta
                        {
                            MissionId = request.MissionId,
                            StreamId = streamId,
                            SeqInStream = seq++,
                            TextDelta = text.Text,
                        }, ct);
                        break;

                    case AgentApprovalRequiredUpdate:
                        // 承認待ちに入ったら途中経過の配信は打ち切る。再開は一括経路が担う。
                        interrupted = true;
                        break;

                    case AgentCompletedUpdate done:
                        completed = done.Result;
                        break;

                    // AgentToolCallUpdate は現状 UI へ配信しない (将来のツール実行表示のために型だけ用意してある)。
                }
            }
        }
        finally
        {
            if (started)
            {
                // キャンセルされた場合も暫定表示は必ず閉じる。
                await sink.CompletedAsync(new AgentStreamCompleted
                {
                    MissionId = request.MissionId,
                    StreamId = streamId,
                    Interrupted = interrupted,
                }, CancellationToken.None);
            }
        }

        return completed
            ?? throw new InvalidOperationException(
                $"agent '{instance.AgentName}' did not produce a completed streaming result.");
    }

    private async Task<Message> PublishAsync(
        TeamExecutionRequest request,
        AgentInstance sender,
        MessageKind kind,
        string body,
        string? recipient = null,
        string? inReplyTo = null,
        int depth = 0,
        string? costRecordId = null,
        CancellationToken ct = default)
        => await _messages.SendAsync(
            request.MissionId,
            MessageSenderKind.Agent,
            kind,
            body,
            sender.InstanceId,
            recipient,
            inReplyTo: inReplyTo,
            delegationDepth: depth,
            costRecordId: costRecordId,
            ct: ct);

    private async Task<string?> RecordCostAsync(
        AgentInstance instance,
        AgentInvocationResult result,
        TeamExecutionRequest request,
        CancellationToken ct)
    {
        if (_costs is null)
        {
            return null;
        }
        var record = await _costs.RecordTurnAsync(
            instance.AgentName,
            result.ModelName,
            request.MissionId,
            instance.InstanceId,
            nodeRunId: null,
            iterationId: null,
            result.InputTokens,
            result.OutputTokens,
            ct);
        return record.CostRecordId;
    }

    private async Task SetStateAsync(
        AgentInstance instance,
        AgentInstanceState state,
        CancellationToken ct,
        string? awaitingInstanceId = null)
    {
        if (_instances is not null && instance.State != state)
        {
            await _instances.SetStateAsync(instance.InstanceId, state, awaitingInstanceId, ct);
        }
        instance = instance with { State = state, AwaitingInstanceId = awaitingInstanceId };
        var handlers = StateChanged;
        if (handlers is not null)
        {
            foreach (var handler in handlers.GetInvocationList().Cast<Func<AgentInstance, Task>>())
            {
                try { await handler(instance); }
                catch (Exception ex) { _logger?.LogWarning(ex, "agent state subscriber failed instance={InstanceId}", instance.InstanceId); }
            }
        }
    }

    private static Dictionary<string, string> ParseCall(string? args)
    {
        if (string.IsNullOrWhiteSpace(args))
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
        try
        {
            using var document = JsonDocument.Parse(args);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }
            return document.RootElement.EnumerateObject().ToDictionary(
                property => property.Name,
                property => property.Value.ValueKind == JsonValueKind.String
                    ? property.Value.GetString() ?? string.Empty
                    : property.Value.ToString(),
                StringComparer.OrdinalIgnoreCase);
        }
        catch (JsonException)
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["instruction"] = args,
            };
        }
    }

    private static string? Value(IReadOnlyDictionary<string, string> values, string key)
        => values.TryGetValue(key, out var value) ? value : null;
}
