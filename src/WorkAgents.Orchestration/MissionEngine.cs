using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using WorkAgents.Core;
using WorkAgents.Core.Abstractions;
using WorkAgents.Core.Missions;
using WorkAgents.Core.Teams;
using WorkAgents.Core.Graphs;
using WorkAgents.Orchestration.Admission;
using WorkAgents.Orchestration.Graph;
using WorkAgents.Orchestration.Teams;

namespace WorkAgents.Orchestration;

public sealed record MissionStatusChangedEvent(
    string MissionId,
    MissionStatus Status,
    MissionOutcome? Outcome,
    MissionStopReason? StopReason,
    MissionQueuedReason? QueuedReason,
    int? QueuePosition,
    DateTimeOffset ChangedAt);

/// <summary>
/// Mission admission and execution coordinator.
/// </summary>
public sealed class MissionEngine
{
    private readonly IMissionStore _missionStore;
    private readonly AdmissionController _admission;
    private readonly ILogger<MissionEngine> _logger;
    private readonly TeamExecutor? _teamExecutor;
    private readonly IReadOnlyList<TeamDefinition> _teams;
    private readonly GraphExecutor? _graphExecutor;
    private readonly IReadOnlyList<GraphDefinition> _graphs;
    private readonly IAgentInstanceStore? _instanceStore;
    private readonly IWorkflowScriptRunner? _scriptRunner;
    private readonly IMissionWorkspaceProvider? _workspaceProvider;
    private readonly IMissionCancellationRegistry? _cancellationRegistry;
    private readonly IApprovalService? _approvalService;
    private readonly ConcurrentDictionary<string, Task> _running = new(StringComparer.Ordinal);

    public event Func<MissionStatusChangedEvent, Task>? StatusChanged;

    public MissionEngine(
        IMissionStore missionStore,
        AdmissionController admission,
        ILogger<MissionEngine> logger,
        TeamExecutor? teamExecutor = null,
        IReadOnlyList<TeamDefinition>? teams = null,
        IAgentInstanceStore? instanceStore = null,
        GraphExecutor? graphExecutor = null,
        IReadOnlyList<GraphDefinition>? graphs = null,
        IWorkflowScriptRunner? scriptRunner = null,
        IMissionWorkspaceProvider? workspaceProvider = null,
        IMissionCancellationRegistry? cancellationRegistry = null,
        IApprovalService? approvalService = null)
    {
        _missionStore = missionStore;
        _admission = admission;
        _logger = logger;
        _teamExecutor = teamExecutor;
        _teams = teams ?? Array.Empty<TeamDefinition>();
        _instanceStore = instanceStore;
        _graphExecutor = graphExecutor;
        _graphs = graphs ?? Array.Empty<GraphDefinition>();
        _scriptRunner = scriptRunner;
        _workspaceProvider = workspaceProvider;
        _cancellationRegistry = cancellationRegistry;
        _approvalService = approvalService;
    }

    /// <summary>ミッションを受け付ける。上限に空きがあれば即座に開始し、無ければ待機列へ入れる。</summary>
    public async Task<Mission> SubmitAsync(Mission mission, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(mission);
        await _missionStore.CreateAsync(mission, ct);

        var admission = await _admission.RequestMissionAsync(mission.MissionId, ct);
        if (admission.Admitted)
        {
            await StartAsync(mission.MissionId, ct);
        }
        else
        {
            await _missionStore.SetQueuePositionAsync(
                mission.MissionId, admission.Reason, admission.QueuePosition, ct);
            _logger.LogInformation(
                "mission {MissionId} queued at position {Position} (reason={Reason})",
                mission.MissionId, admission.QueuePosition, admission.Reason);
        }

        return await _missionStore.GetAsync(mission.MissionId, ct) ?? mission;
    }

    /// <summary>待機列から昇格したミッション、または受付直後のミッションを実行状態にする。</summary>
    public async Task StartAsync(string missionId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(missionId);

        await _missionStore.SetQueuePositionAsync(missionId, null, null, ct);
        await _missionStore.SetStatusAsync(missionId, MissionStatus.Running, ct: ct);
        _logger.LogInformation("mission {MissionId} started", missionId);
        await PublishStatusAsync(missionId, ct);

        var mission = await _missionStore.GetAsync(missionId, ct);
        if (mission is null)
        {
            return;
        }

        if (mission.TargetKind == MissionTargetKind.Graph && _graphExecutor is not null)
        {
            var graph = _graphs.FirstOrDefault(candidate => string.Equals(candidate.Name, mission.TargetName, StringComparison.OrdinalIgnoreCase));
            if (graph is null)
            {
                await CompleteAsync(missionId, MissionStatus.Failed, MissionOutcome.Failed, MissionStopReason.OrchestratorFailure, "The configured graph could not be resolved.", CancellationToken.None);
                return;
            }
            var graphWorkspace = await PrepareWorkspaceAsync(mission, ct);
            if (!graphWorkspace.Prepared)
            {
                return;
            }
            var graphExecutionToken = _cancellationRegistry?.Register(missionId) ?? CancellationToken.None;
            var graphExecution = ExecuteGraphAsync(mission, graph, graphWorkspace.Path, graphExecutionToken);
            if (_running.TryAdd(missionId, graphExecution))
            {
                _ = graphExecution.ContinueWith(
                    completedTask =>
                    {
                        _running.TryRemove(missionId, out var removedTask);
                        _cancellationRegistry?.Remove(missionId);
                    },
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
            }
            return;
        }
        if (mission.TargetKind != MissionTargetKind.Team || _teamExecutor is null)
        {
            return;
        }

        var team = _teams.FirstOrDefault(candidate =>
            string.Equals(candidate.Name, mission.TargetName, StringComparison.OrdinalIgnoreCase));
        if (team is null)
        {
            await CompleteAsync(
                missionId,
                MissionStatus.Failed,
                MissionOutcome.Failed,
                MissionStopReason.OrchestratorFailure,
                "The configured team could not be resolved.",
                CancellationToken.None);
            return;
        }

        var teamWorkspace = await PrepareWorkspaceAsync(mission, ct);
        if (!teamWorkspace.Prepared)
        {
            return;
        }

        var teamExecutionToken = _cancellationRegistry?.Register(missionId) ?? CancellationToken.None;
        var execution = ExecuteTeamAsync(mission, team, teamWorkspace.Path, teamExecutionToken);
        if (!_running.TryAdd(missionId, execution))
        {
            return;
        }
        _ = execution.ContinueWith(
            completedTask =>
            {
                _running.TryRemove(missionId, out var removedTask);
                _cancellationRegistry?.Remove(missionId);
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    public Task? GetExecutionTask(string missionId)
        => _running.TryGetValue(missionId, out var task) ? task : null;

    private static readonly HashSet<MissionStatus> TerminalStatuses = new()
    {
        MissionStatus.Succeeded,
        MissionStatus.NotConverged,
        MissionStatus.Failed,
        MissionStatus.Aborted,
    };

    /// <summary>
    /// 完了済みのTeamミッションに人の割り込みが届いたとき、チーム実行を再始動する。
    /// 全エージェントが「完了・待機」のまま割り込みが誰にも処理されない不具合への対応。
    /// 実行中(_runningに存在)の場合は次のターンで既存のContextAssemblerが割り込みを自然に拾うため何もしない。
    /// </summary>
    public async Task<bool> TryResumeFromInterventionAsync(string missionId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(missionId);

        if (_teamExecutor is null || _running.ContainsKey(missionId))
        {
            return false;
        }

        var mission = await _missionStore.GetAsync(missionId, ct);
        if (mission is null || mission.TargetKind != MissionTargetKind.Team || !TerminalStatuses.Contains(mission.Status))
        {
            return false;
        }

        var team = _teams.FirstOrDefault(candidate =>
            string.Equals(candidate.Name, mission.TargetName, StringComparison.OrdinalIgnoreCase));
        if (team is null)
        {
            return false;
        }

        var workspace = await PrepareWorkspaceAsync(mission, ct);
        if (!workspace.Prepared)
        {
            return false;
        }

        await _missionStore.SetStatusAsync(missionId, MissionStatus.Running, ct: ct);
        _logger.LogInformation("mission {MissionId} resumed by human intervention", missionId);
        await PublishStatusAsync(missionId, ct);

        var resumeExecutionToken = _cancellationRegistry?.Register(missionId) ?? CancellationToken.None;
        var execution = ExecuteTeamAsync(mission, team, workspace.Path, resumeExecutionToken);
        if (!_running.TryAdd(missionId, execution))
        {
            return false;
        }
        _ = execution.ContinueWith(
            completedTask =>
            {
                _running.TryRemove(missionId, out var removedTask);
                _cancellationRegistry?.Remove(missionId);
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        return true;
    }

    public Task ConfigureBudgetAsync(string missionId, Budget budget, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(missionId);
        ArgumentNullException.ThrowIfNull(budget);
        if (!string.Equals(missionId, budget.MissionId, StringComparison.Ordinal))
        {
            throw new ArgumentException("Budget mission ID does not match the mission.", nameof(budget));
        }
        return _missionStore.UpsertBudgetAsync(budget, ct);
    }

    private async Task ExecuteTeamAsync(
        Mission mission,
        TeamDefinition team,
        string? workspacePath,
        CancellationToken executionToken)
    {
        try
        {
            var result = await _teamExecutor!.ExecuteAsync(new TeamExecutionRequest
            {
                MissionId = mission.MissionId,
                Goal = mission.Goal,
                Team = team,
                WorkingDirectory = workspacePath,
            }, executionToken);
            var status = result.Outcome switch
            {
                MissionOutcome.Succeeded => MissionStatus.Succeeded,
                MissionOutcome.NotConverged => MissionStatus.NotConverged,
                MissionOutcome.Aborted => MissionStatus.Aborted,
                _ => MissionStatus.Failed,
            };
            await CompleteAsync(mission.MissionId, status, result.Outcome, result.StopReason, ct: CancellationToken.None);
        }
        catch (OperationCanceledException) when (executionToken.IsCancellationRequested)
        {
            await CompleteCancellationIfNeededAsync(mission.MissionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "mission execution failed mission={MissionId}", mission.MissionId);
            try
            {
                await CompleteAsync(
                    mission.MissionId,
                    MissionStatus.Failed,
                    MissionOutcome.Failed,
                    MissionStopReason.OrchestratorFailure,
                    "Mission execution failed.",
                    CancellationToken.None);
            }
            catch (Exception completionError)
            {
                _logger.LogError(completionError, "failed to persist mission failure mission={MissionId}", mission.MissionId);
            }
        }
    }

    private async Task ExecuteGraphAsync(
        Mission mission,
        GraphDefinition graph,
        string? workspacePath,
        CancellationToken executionToken)
    {
        try
        {
            await _graphExecutor!.ExecuteAsync(new GraphExecutionRequest
            {
                MissionId = mission.MissionId,
                Goal = mission.Goal,
                Graph = graph,
                WorkingDirectory = workspacePath,
                CodeHandler = _scriptRunner is null
                    ? null
                    : (node, input, ct) => RunGraphCodeNodeAsync(graph, node, input, ct),
                TeamHandler = _teamExecutor is null
                    ? null
                    : (node, input, ct) => ExecuteGraphTeamNodeAsync(mission, node, input, workspacePath, ct),
                ApprovalHandler = _approvalService is null
                    ? null
                    : (node, input, ct) => ExecuteGraphApprovalNodeAsync(mission, node, ct),
            }, executionToken);
            await CompleteAsync(mission.MissionId, MissionStatus.Succeeded, MissionOutcome.Succeeded, MissionStopReason.StopConditionMet, ct: CancellationToken.None);
        }
        catch (OperationCanceledException) when (executionToken.IsCancellationRequested)
        {
            await CompleteCancellationIfNeededAsync(mission.MissionId);
        }
        catch (Exception ex)
        {
            var current = await _missionStore.GetAsync(mission.MissionId, CancellationToken.None);
            if (current?.Status == MissionStatus.Aborted)
            {
                return;
            }

            _logger.LogError(ex, "graph mission execution failed mission={MissionId}", mission.MissionId);
            try
            {
                await CompleteAsync(mission.MissionId, MissionStatus.Failed, MissionOutcome.Failed, MissionStopReason.OrchestratorFailure, "Graph execution failed.", CancellationToken.None);
            }
            catch (Exception completionError)
            {
                _logger.LogError(completionError, "failed to persist graph mission failure mission={MissionId}", mission.MissionId);
            }
        }
    }

    private async Task<string> ExecuteGraphApprovalNodeAsync(
        Mission mission,
        GraphNode node,
        CancellationToken ct)
    {
        if (_approvalService is null)
        {
            throw new InvalidOperationException("Graph approval service is not configured.");
        }

        await _missionStore.SetStatusAsync(mission.MissionId, MissionStatus.AwaitingApproval, ct: ct);
        await PublishStatusAsync(mission.MissionId, ct);

        var approval = await _approvalService.RequestMissionAsync(
            mission.MissionId,
            $"graph:{node.Id}",
            "graph_approval",
            $"graph node {node.Id}",
            TimeSpan.FromSeconds(node.TimeoutSeconds ?? 900),
            title: node.Title ?? "Graph approval",
            ct: ct);
        if (approval.Status != ApprovalStatus.Approved)
        {
            await _missionStore.SetStatusAsync(
                mission.MissionId,
                MissionStatus.Aborted,
                MissionOutcome.Aborted,
                MissionStopReason.UserAbort,
                error: "Graph approval was not approved.",
                ct: CancellationToken.None);
            await PublishStatusAsync(mission.MissionId, CancellationToken.None);
            throw new InvalidOperationException("Graph approval was not approved.");
        }

        await _missionStore.SetStatusAsync(mission.MissionId, MissionStatus.Running, ct: CancellationToken.None);
        await PublishStatusAsync(mission.MissionId, CancellationToken.None);
        return "approved";
    }

    private async Task<(bool Prepared, string? Path)> PrepareWorkspaceAsync(Mission mission, CancellationToken ct)
    {
        if (_workspaceProvider is null)
        {
            return (true, null);
        }

        try
        {
            return (true, await _workspaceProvider.PrepareAsync(mission.MissionId, ct));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "mission workspace preparation failed mission={MissionId}", mission.MissionId);
            try
            {
                await CompleteAsync(
                    mission.MissionId,
                    MissionStatus.Failed,
                    MissionOutcome.Failed,
                    MissionStopReason.OrchestratorFailure,
                    "Mission workspace could not be prepared.",
                    CancellationToken.None);
            }
            catch (Exception completionError)
            {
                _logger.LogError(completionError, "failed to persist workspace preparation failure mission={MissionId}", mission.MissionId);
            }
            return (false, null);
        }
    }

    private async Task<string> ExecuteGraphTeamNodeAsync(
        Mission mission,
        GraphNode node,
        string input,
        string? workspacePath,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(node.Team))
        {
            throw new InvalidOperationException($"Graph team node '{node.Id}' has no team.");
        }

        var team = _teams.FirstOrDefault(candidate =>
            string.Equals(candidate.Name, node.Team, StringComparison.OrdinalIgnoreCase));
        if (team is null)
        {
            throw new InvalidOperationException($"Graph team node '{node.Id}' references unknown team '{node.Team}'.");
        }

        var result = await _teamExecutor!.ExecuteAsync(new TeamExecutionRequest
        {
            MissionId = mission.MissionId,
            Goal = input,
            Team = team,
            WorkingDirectory = workspacePath,
        }, ct);
        if (result.Outcome != MissionOutcome.Succeeded)
        {
            throw new InvalidOperationException($"Graph team node '{node.Id}' failed.");
        }

        return result.Messages.LastOrDefault()?.Body ?? input;
    }

    /// <summary>
    /// graph.yaml の kind: code ノードを実行する。codeFile (グラフフォルダーからの相対パス) を読み、
    /// workflow.yaml の code ステップと同じ <see cref="IWorkflowScriptRunner"/> (Roslyn) で評価する。
    /// スクリプトからは Inputs["input"] で直前ノードまでの入力文字列を参照できる。
    /// </summary>
    private async Task<string> RunGraphCodeNodeAsync(GraphDefinition graph, GraphNode node, string input, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(node.CodeFile))
        {
            throw new InvalidOperationException($"graph '{graph.Name}' node '{node.Id}' requires codeFile.");
        }
        var path = Path.IsPathRooted(node.CodeFile) ? node.CodeFile : Path.Combine(graph.FolderPath, node.CodeFile);
        if (!File.Exists(path))
        {
            throw new InvalidOperationException($"graph '{graph.Name}' node '{node.Id}' codeFile not found: '{path}'.");
        }
        var code = await File.ReadAllTextAsync(path, ct);
        var inputs = new Dictionary<string, object?>(StringComparer.Ordinal) { ["input"] = input };
        var raw = await _scriptRunner!.RunAsync(code, inputs, ct);
        return raw switch
        {
            null => string.Empty,
            string text => text,
            _ => JsonSerializer.Serialize(raw),
        };
    }

    /// <summary>ミッションを終端状態へ遷移させ、待機列があれば次を昇格させる。</summary>
    public async Task CompleteAsync(
        string missionId,
        MissionStatus status,
        MissionOutcome? outcome,
        MissionStopReason? stopReason = null,
        string? error = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(missionId);

        await _missionStore.SetStatusAsync(missionId, status, outcome, stopReason, error, ct);
        _logger.LogInformation("mission {MissionId} completed with status {Status}", missionId, status);
        await PublishStatusAsync(missionId, ct);

        var promoted = await _admission.ReleaseMissionAsync(missionId, ct);
        foreach (var promotedId in promoted)
        {
            await StartAsync(promotedId, ct);
        }
    }

    /// <summary>ミッションを中断する。待機中であればそのまま待機列から取り除いて中断とする。</summary>
    public async Task AbortAsync(string missionId, CancellationToken ct = default)
    {
        var mission = await _missionStore.GetAsync(missionId, ct)
            ?? throw new KeyNotFoundException($"Mission not found: '{missionId}'.");

        if (mission.Status == MissionStatus.Queued)
        {
            await _admission.RemoveFromQueueAsync(missionId, ct);
            await _missionStore.SetStatusAsync(missionId, MissionStatus.Aborted, MissionOutcome.Aborted, MissionStopReason.UserAbort, ct: ct);
            return;
        }

        _cancellationRegistry?.TryCancel(missionId);
        await CompleteAsync(missionId, MissionStatus.Aborted, MissionOutcome.Aborted, MissionStopReason.UserAbort, ct: ct);
    }

    private async Task CompleteCancellationIfNeededAsync(string missionId)
    {
        var mission = await _missionStore.GetAsync(missionId, CancellationToken.None);
        if (mission is null || TerminalStatuses.Contains(mission.Status))
        {
            return;
        }

        await CompleteAsync(
            missionId,
            MissionStatus.Aborted,
            MissionOutcome.Aborted,
            MissionStopReason.UserAbort,
            ct: CancellationToken.None);
    }

    public async Task PauseAsync(string missionId, CancellationToken ct = default)
    {
        var mission = await _missionStore.GetAsync(missionId, ct)
            ?? throw new KeyNotFoundException($"Mission not found: '{missionId}'.");
        if (mission.Status != MissionStatus.Running)
        {
            throw new InvalidOperationException($"Mission '{missionId}' is not running.");
        }
        await _missionStore.SetStatusAsync(missionId, MissionStatus.Paused, ct: ct);
        await PublishStatusAsync(missionId, ct);
    }

    public async Task ResumeAsync(string missionId, CancellationToken ct = default)
    {
        var mission = await _missionStore.GetAsync(missionId, ct)
            ?? throw new KeyNotFoundException($"Mission not found: '{missionId}'.");
        if (mission.Status != MissionStatus.Paused)
        {
            throw new InvalidOperationException($"Mission '{missionId}' is not paused.");
        }
        await _missionStore.SetStatusAsync(missionId, MissionStatus.Running, ct: ct);
        await PublishStatusAsync(missionId, ct);
    }

    public async Task StopAgentAsync(string missionId, string instanceId, CancellationToken ct = default)
    {
        if (_instanceStore is null)
        {
            throw new InvalidOperationException("Agent instance control is not configured.");
        }
        var instance = await _instanceStore.GetAsync(instanceId, ct)
            ?? throw new KeyNotFoundException($"Agent instance not found: '{instanceId}'.");
        if (!string.Equals(instance.MissionId, missionId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Agent instance does not belong to the mission.");
        }
        if (instance.State is not (AgentInstanceState.Completed or AgentInstanceState.Failed or AgentInstanceState.Stopped))
        {
            await _instanceStore.SetStateAsync(instanceId, AgentInstanceState.Stopped, ct: ct);
        }
        await _instanceStore.SetLeftAsync(instanceId, "Stopped by user.", ct);
    }

    private async Task PublishStatusAsync(string missionId, CancellationToken ct)
    {
        var mission = await _missionStore.GetAsync(missionId, ct);
        var handlers = StatusChanged;
        if (mission is null || handlers is null)
        {
            return;
        }
        var notification = new MissionStatusChangedEvent(
            mission.MissionId,
            mission.Status,
            mission.Outcome,
            mission.StopReason,
            mission.QueuedReason,
            mission.QueuePosition,
            DateTimeOffset.UtcNow);
        foreach (var handler in handlers.GetInvocationList().Cast<Func<MissionStatusChangedEvent, Task>>())
        {
            try
            {
                await handler(notification);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "mission status subscriber failed mission={MissionId}", missionId);
            }
        }
    }

    /// <summary>容量に空きがある限り、待機列から昇格させて開始する (T041 の再走査で使う)。</summary>
    public async Task PumpQueueAsync(CancellationToken ct = default)
    {
        while (!ct.IsCancellationRequested)
        {
            var promoted = await _admission.TryPromoteFromQueueAsync(ct);
            if (promoted is null)
            {
                break;
            }

            await StartAsync(promoted, ct);
        }
    }
}
