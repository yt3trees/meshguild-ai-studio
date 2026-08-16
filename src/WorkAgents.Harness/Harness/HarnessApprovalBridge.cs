using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using WorkAgents.Core;
using WorkAgents.Core.Abstractions;

namespace WorkAgents.Harness.Harness;

/// <summary>
/// MAFの承認要求をWorkAgentsの永続承認へ変換し、承認後に同一セッションを再開する。
/// </summary>
public sealed class HarnessApprovalBridge
{
    private readonly IApprovalService _approvalService;
    private readonly ILogger<HarnessApprovalBridge>? _logger;

    public HarnessApprovalBridge(
        IApprovalService approvalService,
        ILogger<HarnessApprovalBridge>? logger = null)
    {
        _approvalService = approvalService;
        _logger = logger;
    }

    public async Task<AgentResponse> ResumeAsync(
        string runId,
        AIAgent agent,
        AgentSession session,
        AgentResponse response,
        TimeSpan timeout,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        ArgumentNullException.ThrowIfNull(agent);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(response);

        var requests = GetApprovalRequests(response);
        if (requests.Count == 0)
        {
            return response;
        }

        var responses = new List<AIContent>(requests.Count);
        foreach (var request in requests)
        {
            var toolCall = DescribeToolCall(request.ToolCall);
            var decision = await _approvalService.RequestAsync(
                runId,
                toolCall.Name,
                toolCall.Arguments,
                timeout,
                ct);

            if (decision.Status != ApprovalStatus.Approved)
            {
                throw new ApprovalRejectedException(decision);
            }

            responses.Add(request.CreateResponse(approved: true, decision.DecisionReason));
        }

        _logger?.LogInformation(
            "resuming approved harness run={RunId} approvalCount={ApprovalCount}",
            runId,
            responses.Count);
        var approvalMessage = new ChatMessage(ChatRole.User, responses);
        return await agent.RunAsync(approvalMessage, session, cancellationToken: ct);
    }

    public Task<ApprovalRequest> RequestMissionApprovalAsync(
        string missionId,
        string agentInstanceId,
        string tool,
        string argsSummary,
        TimeSpan timeout,
        string? title = null,
        string? nodeRunId = null,
        string? iterationId = null,
        CancellationToken ct = default)
        => _approvalService.RequestMissionAsync(
            missionId,
            agentInstanceId,
            tool,
            argsSummary,
            timeout,
            title,
            nodeRunId,
            iterationId,
            ct);

    public static void RevalidateApprovedOperation(ApprovalRequest request, string tool, string argsSummary)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(tool);
        ArgumentNullException.ThrowIfNull(argsSummary);
        if (request.Status != ApprovalStatus.Approved || !string.Equals(request.Tool, tool, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The approved operation no longer matches the requested operation.");
        }
    }

    public static IReadOnlyList<ToolApprovalRequestContent> GetApprovalRequests(AgentResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);
        return response.Messages
            .SelectMany(message => message.Contents)
            .OfType<ToolApprovalRequestContent>()
            .ToArray();
    }

    private static (string Name, string Arguments) DescribeToolCall(ToolCallContent toolCall)
    {
        if (toolCall is FunctionCallContent functionCall)
        {
            var arguments = functionCall.Arguments is null
                ? string.Empty
                : JsonSerializer.Serialize(functionCall.Arguments);
            return (functionCall.Name, arguments);
        }

        return (toolCall.GetType().Name, toolCall.CallId);
    }
}
