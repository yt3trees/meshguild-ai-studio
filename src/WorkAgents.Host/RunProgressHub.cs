using Microsoft.AspNetCore.SignalR;
using WorkAgents.Core;
using WorkAgents.Core.Abstractions;

namespace WorkAgents.Host;

public sealed class RunProgressHub : Hub
{
    public static string GroupName(string runId) => $"run:{runId}";

    public Task Subscribe(string runId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        return Groups.AddToGroupAsync(Context.ConnectionId, GroupName(runId));
    }
}

public sealed class SignalRRunProgressPublisher : IRunProgressPublisher
{
    private readonly IHubContext<RunProgressHub> _hubContext;

    public SignalRRunProgressPublisher(IHubContext<RunProgressHub> hubContext)
    {
        _hubContext = hubContext;
    }

    public Task PublishAsync(RunRecord run, CancellationToken ct = default)
    {
        return _hubContext.Clients
            .Group(RunProgressHub.GroupName(run.RunId))
            .SendAsync("runUpdated", new
            {
                run.RunId,
                run.Status,
                run.StartedAt,
                run.CompletedAt,
                run.Result,
                run.Error,
            }, ct);
    }
}