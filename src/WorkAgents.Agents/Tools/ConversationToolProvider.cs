using System.Text.Json;
using Microsoft.Extensions.AI;

namespace WorkAgents.Agents.Tools;

public sealed record ConversationToolResult(
    bool Rejected,
    string Code,
    string MessageId = "",
    string? AssignedInstanceId = null,
    string? Answer = null)
{
    public static ConversationToolResult Accepted(string messageId = "", string? answer = null, string? assignedInstanceId = null)
        => new(false, "ok", messageId, assignedInstanceId, answer);

    public static ConversationToolResult RejectedResult(string code)
        => new(true, code);
}

/// <summary>Callback used by conversation functions to enter the orchestration message path.</summary>
public delegate Task<ConversationToolResult> ConversationToolHandler(
    string callerAgent,
    string toolName,
    string argumentsJson,
    CancellationToken ct);

/// <summary>
/// Creates the conversation functions exposed to an orchestrator or a team member.
/// The provider deliberately contains no secret-bearing defaults or descriptions.
/// </summary>
public sealed class ConversationToolProvider
{
    private static readonly IReadOnlyDictionary<string, string> Descriptions =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["delegate_task"] = "Delegate a bounded task to a member of the current team.",
            ["ask_agent"] = "Ask an allowed team participant a question and wait for an answer.",
            ["answer_agent"] = "Answer a question from another team participant.",
            ["share_finding"] = "Share a finding with the team or an allowed participant.",
            ["handoff_task"] = "Hand off remaining work to an allowed team participant.",
            ["report_result"] = "Report the result of delegated work to the orchestrator.",
            ["add_participant"] = "Add a definition-backed participant to the running team.",
            ["remove_participant"] = "Remove an idle participant from the running team.",
            ["scale_agent"] = "Change the number of running instances within the declared limit.",
            ["finish_mission"] = "Declare that the orchestrator believes the mission is complete.",
        };

    public IReadOnlyList<AgentToolRegistration> CreateTools(
        string agentName,
        bool orchestrator,
        ConversationToolHandler? handler = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentName);
        handler ??= static (_, _, _, _) => Task.FromResult(ConversationToolResult.RejectedResult("runtime_not_connected"));

        var names = orchestrator
            ? new[] { "delegate_task", "ask_agent", "answer_agent", "share_finding", "handoff_task", "add_participant", "remove_participant", "scale_agent", "finish_mission" }
            : new[] { "ask_agent", "answer_agent", "share_finding", "handoff_task", "report_result" };

        return names.Select(name =>
        {
            var description = Descriptions[name];
            var function = AIFunctionFactory.Create(
                (Func<string, CancellationToken, Task<ConversationToolResult>>)((arguments, ct) =>
                    handler(agentName, name, arguments, ct)),
                name,
                description,
                null);
            return new AgentToolRegistration(name, description, "conversation", "automatic", function);
        }).ToArray();
    }

    public static string SerializeArguments(IReadOnlyDictionary<string, object?> arguments)
        => JsonSerializer.Serialize(arguments);
}
