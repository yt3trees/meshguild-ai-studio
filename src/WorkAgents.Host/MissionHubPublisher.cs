using Microsoft.AspNetCore.SignalR;
using WorkAgents.Orchestration.Teams;
using WorkAgents.Orchestration;
using WorkAgents.Infrastructure.Approvals;
using WorkAgents.Orchestration.Loops;
using WorkAgents.Orchestration.Graph;

namespace WorkAgents.Host;

/// <summary>Bridges committed orchestration events to the mission SignalR groups.</summary>
public sealed class MissionHubPublisher
{
    private readonly IHubContext<MissionHub> _hub;

    public MissionHubPublisher(MessageBus messages, MissionEngine engine, TeamExecutor teamExecutor, LoopExecutor loops, GraphExecutor graphs, ApprovalService approvals, IHubContext<MissionHub> hub)
    {
        ArgumentNullException.ThrowIfNull(messages);
        ArgumentNullException.ThrowIfNull(hub);
        _hub = hub;
        messages.Published += PublishMessageAsync;
        engine.StatusChanged += PublishStatusAsync;
        teamExecutor.StateChanged += PublishAgentStateAsync;
        loops.IterationEvaluated += PublishIterationAsync;
        loops.BudgetUpdated += PublishBudgetAsync;
        graphs.NodeStateChanged += PublishNodeStateAsync;
        graphs.EdgeTransited += PublishEdgeAsync;
        approvals.Requested += PublishApprovalRequestedAsync;
        approvals.Decided += PublishApprovalDecidedAsync;
    }

    private async Task PublishMessageAsync(MessagePublished published)
    {
        var group = _hub.Clients.Group(MissionHub.GroupName(published.Message.MissionId));
        await group.SendAsync("MessageAppended", published.Message);
        if (published.Message.Kind == WorkAgents.Core.Missions.MessageKind.RosterChange)
        {
            await group.SendAsync("RosterChanged", new { missionId = published.Message.MissionId, messageId = published.Message.MessageId, reason = published.Message.Body });
        }
    }

    private Task PublishStatusAsync(MissionStatusChangedEvent changed)
        => _hub.Clients
            .Group(MissionHub.GroupName(changed.MissionId))
            .SendAsync("MissionStatusChanged", changed);

    private Task PublishApprovalRequestedAsync(WorkAgents.Core.ApprovalRequest request)
        => request.MissionId is null
            ? Task.CompletedTask
            : _hub.Clients.Group(MissionHub.GroupName(request.MissionId)).SendAsync("ApprovalRequested", request);

    private Task PublishApprovalDecidedAsync(WorkAgents.Core.ApprovalRequest request)
        => request.MissionId is null
            ? Task.CompletedTask
            : _hub.Clients.Group(MissionHub.GroupName(request.MissionId)).SendAsync("ApprovalDecided", request);

    private Task PublishAgentStateAsync(WorkAgents.Core.Missions.AgentInstance instance)
        => _hub.Clients.Group(MissionHub.GroupName(instance.MissionId)).SendAsync("AgentStateChanged", instance);

    private Task PublishIterationAsync(IterationEvaluatedEvent value)
        => _hub.Clients.Group(MissionHub.GroupName(value.MissionId)).SendAsync("IterationEvaluated", value);

    private Task PublishBudgetAsync(BudgetUpdatedEvent value)
        => _hub.Clients.Group(MissionHub.GroupName(value.MissionId)).SendAsync("BudgetUpdated", value);

    private Task PublishNodeStateAsync(NodeStateChangedEvent value)
        => _hub.Clients.Group(MissionHub.GroupName(value.MissionId)).SendAsync("NodeStateChanged", value);

    private Task PublishEdgeAsync(EdgeTransitedEvent value)
        => _hub.Clients.Group(MissionHub.GroupName(value.Transit.MissionId)).SendAsync("EdgeTransited", value.Transit);
}
