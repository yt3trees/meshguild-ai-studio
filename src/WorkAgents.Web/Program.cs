using WorkAgents.Agents.DependencyInjection;
using WorkAgents.Infrastructure.DependencyInjection;
using WorkAgents.Infrastructure.Telemetry;
using WorkAgents.Core;
using WorkAgents.Core.Abstractions;
using WorkAgents.Core.Loops;
using WorkAgents.Core.Missions;
using WorkAgents.Core.Triggers;
using WorkAgents.Web.Components;
using WorkAgents.Web.Services;
using WorkAgents.Orchestration.Teams;

var builder = WebApplication.CreateBuilder(args);

if (builder.Environment.IsEnvironment("E2E"))
{
    builder.WebHost.UseStaticWebAssets();
}

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddWorkAgentsInfrastructure(builder.Configuration);
builder.Services.AddWorkAgentsTelemetry(builder.Configuration, "WorkAgents.Web");
// LLM(MAF)とエージェントの配線(M1)。モデルと認証はModels画面でプロバイダーごとに設定。
builder.Services.AddWorkAgentsAgents(builder.Configuration);
builder.Services.AddSingleton<MissionHubClient>();
builder.Services.AddSingleton<MissionApiClient>();
// 定義 (team.yaml / graph.yaml) を GUI から編集するための読み書き口。
builder.Services.AddSingleton<DefinitionAuthoringService>();
builder.Services.AddSingleton<DefinitionDraftService>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForErrors: true);

app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

if (app.Environment.IsEnvironment("E2E"))
{
    app.MapPost("/__e2e/approvals", async (
        E2eApprovalSeedRequest request,
        IApprovalStore approvalStore,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken) =>
    {
        if (!request.IsValid)
        {
            return Results.BadRequest(new { error = "Invalid approval seed request." });
        }

        try
        {
            var approval = ApprovalRequest.Create(
                request.RunId!.Trim(),
                request.Tool!.Trim(),
                request.ArgsSummary!.Trim(),
                TimeSpan.FromSeconds(request.TimeoutSeconds));
            await approvalStore.CreateAsync(approval, cancellationToken);
            return Results.Created(
                $"/__e2e/approvals/{approval.ApprovalId}",
                new { approvalId = approval.ApprovalId, status = approval.Status.ToString() });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            loggerFactory.CreateLogger("E2eApprovalEndpoints")
                .LogError(exception, "E2E approval seed failed.");
            return Results.Problem(statusCode: 500, title: "E2E approval seed failed.");
        }
    }).RequireHost("127.0.0.1");

    app.MapGet("/__e2e/approvals/{approvalId}", async (
        string approvalId,
        IApprovalStore approvalStore,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken) =>
    {
        if (string.IsNullOrWhiteSpace(approvalId) || approvalId.Length > 128)
        {
            return Results.BadRequest(new { error = "Invalid approval ID." });
        }

        try
        {
            var approval = await approvalStore.GetAsync(approvalId, cancellationToken);
            return approval is null
                ? Results.NotFound()
                : Results.Ok(new
                {
                    approvalId = approval.ApprovalId,
                    status = approval.Status.ToString(),
                    runId = approval.RunId,
                    tool = approval.Tool,
                });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            loggerFactory.CreateLogger("E2eApprovalEndpoints")
                .LogError(exception, "E2E approval status lookup failed.");
            return Results.Problem(statusCode: 500, title: "E2E approval status lookup failed.");
        }
    }).RequireHost("127.0.0.1");

    app.MapPost("/__e2e/orchestration", SeedE2eOrchestrationAsync).RequireHost("127.0.0.1");

    // The E2E Web process has no separate Host process. Keep the same message
    // contract locally so Team Room can exercise the real intervention path.
    app.MapPost("/missions/{missionId}/messages", async (
        string missionId,
        MissionMessageSubmission request,
        IMissionStore missionStore,
        IInterventionStore interventionStore,
        MessageBus bus,
        CancellationToken ct) =>
    {
        if (string.IsNullOrWhiteSpace(request.Body)) return Results.BadRequest();
        if (await missionStore.GetAsync(missionId, ct) is null) return Results.NotFound();
        var message = await bus.SendAsync(missionId, MessageSenderKind.Human, MessageKind.HumanInstruction, request.Body.Trim(), recipientInstanceId: request.TargetInstanceId, ct: ct);
        var intervention = new Intervention
        {
            InterventionId = Guid.NewGuid().ToString("N"),
            MissionId = missionId,
            MessageId = message.MessageId,
            TargetInstanceId = request.TargetInstanceId,
            Body = message.Body,
        };
        await interventionStore.CreateAsync(intervention, ct);
        return Results.Accepted($"/missions/{missionId}/messages", new { messageId = message.MessageId, seq = message.Seq });
    }).RequireHost("127.0.0.1");
}

static async Task<IResult> SeedE2eOrchestrationAsync(
    E2eOrchestrationSeedRequest request,
    IMissionStore missions,
    IAgentInstanceStore agents,
    IMessageStore messages,
    ILoopStore loops,
    ITriggerStore triggers,
    IApprovalStore approvals,
    IMissionArtifactStore artifacts,
    IMissionWorkspaceProvider workspaceProvider,
    CancellationToken ct)
{
    try
    {
        if (request.Mission is not null)
        {
            var seed = request.Mission;
            var status = Enum.Parse<MissionStatus>(seed.Status, true);
            await missions.CreateAsync(new Mission
            {
                MissionId = seed.MissionId,
                Goal = seed.Goal,
                TargetKind = Enum.Parse<MissionTargetKind>(seed.TargetKind, true),
                TargetName = seed.TargetName,
                TeamName = seed.TeamName,
                Status = status,
                Outcome = string.IsNullOrWhiteSpace(seed.Outcome) ? null : Enum.Parse<MissionOutcome>(seed.Outcome, true),
                StopReason = string.IsNullOrWhiteSpace(seed.StopReason) ? null : Enum.Parse<MissionStopReason>(seed.StopReason, true),
                TriggerKind = Enum.TryParse<MissionTriggerKind>(seed.TriggerKind, true, out var triggerKind) ? triggerKind : MissionTriggerKind.Manual,
                CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
                StartedAt = status == MissionStatus.Queued ? null : DateTimeOffset.UtcNow.AddMinutes(-4),
                CompletedAt = status is MissionStatus.Succeeded or MissionStatus.NotConverged or MissionStatus.Failed or MissionStatus.Aborted ? DateTimeOffset.UtcNow : null,
            }, ct);
            if (seed.Budget is not null)
            {
                await missions.UpsertBudgetAsync(new Budget
                {
                    MissionId = seed.MissionId,
                    CostLimitUsd = seed.Budget.CostLimitUsd,
                    TimeLimitSeconds = seed.Budget.TimeLimitSeconds,
                    MaxIterations = seed.Budget.MaxIterations,
                    MaxConcurrentAgents = seed.Budget.MaxConcurrentAgents,
                    CostUsedUsd = seed.Budget.CostUsedUsd,
                    ElapsedSeconds = seed.Budget.ElapsedSeconds,
                    IterationsUsed = seed.Budget.IterationsUsed,
                    PeakConcurrentAgents = seed.Budget.PeakConcurrentAgents,
                }, ct);
            }
            await workspaceProvider.PrepareAsync(seed.MissionId, ct);
        }

        foreach (var seed in request.Agents ?? [])
        {
            await agents.CreateAsync(new AgentInstance
            {
                InstanceId = seed.InstanceId,
                MissionId = seed.MissionId ?? request.Mission?.MissionId ?? throw new ArgumentException("agent missionId is required"),
                AgentName = seed.AgentName,
                Role = Enum.Parse<AgentInstanceRole>(seed.Role, true),
                InstanceNo = seed.InstanceNo,
                State = Enum.Parse<AgentInstanceState>(seed.State, true),
                AwaitingInstanceId = seed.AwaitingInstanceId,
                ModelName = seed.ModelName,
            }, ct);
        }

        foreach (var seed in request.Messages ?? [])
        {
            await messages.AppendAsync(new Message
            {
                MessageId = seed.MessageId ?? Guid.NewGuid().ToString("N"),
                MissionId = seed.MissionId ?? request.Mission?.MissionId ?? throw new ArgumentException("message missionId is required"),
                Seq = 0,
                SenderKind = Enum.Parse<MessageSenderKind>(seed.SenderKind, true),
                SenderInstanceId = seed.SenderInstanceId,
                RecipientInstanceId = seed.RecipientInstanceId,
                Kind = Enum.Parse<MessageKind>(seed.Kind, true),
                Body = seed.Body,
                DelegationDepth = seed.DelegationDepth,
                InputRefs = seed.InputRefs,
                CostRecordId = seed.CostRecordId,
                CreatedAt = DateTimeOffset.UtcNow.AddSeconds(-seed.SecondsAgo),
            }, ct);
        }

        foreach (var seed in request.Loops ?? [])
        {
            var missionId = seed.MissionId ?? request.Mission?.MissionId ?? throw new ArgumentException("loop missionId is required");
            await loops.CreateLoopRunAsync(new LoopRun
            {
                LoopRunId = seed.LoopRunId,
                MissionId = missionId,
                NodeRunId = seed.NodeRunId,
                MaxIterations = seed.MaxIterations,
                CostLimitUsd = seed.CostLimitUsd,
                TimeLimitSeconds = seed.TimeLimitSeconds,
                ScoreThreshold = seed.ScoreThreshold,
            }, ct);
            foreach (var iterationSeed in seed.Iterations ?? [])
            {
                await loops.CreateIterationAsync(new Iteration
                {
                    IterationId = iterationSeed.IterationId,
                    LoopRunId = seed.LoopRunId,
                    IterationNo = iterationSeed.IterationNo,
                    State = Enum.Parse<IterationState>(iterationSeed.State, true),
                    InputJson = iterationSeed.InputJson,
                    OutputJson = iterationSeed.OutputJson,
                    CostUsd = iterationSeed.CostUsd,
                    Tokens = iterationSeed.Tokens,
                    DurationMs = iterationSeed.DurationMs,
                    CompletedAt = DateTimeOffset.UtcNow.AddMinutes(-iterationSeed.IterationNo),
                }, ct);
                if (iterationSeed.Evaluation is not null)
                {
                    var evaluationSeed = iterationSeed.Evaluation;
                    await loops.AddEvaluationAsync(new Evaluation
                    {
                        EvaluationId = evaluationSeed.EvaluationId,
                        IterationId = iterationSeed.IterationId,
                        Score = evaluationSeed.Score,
                        EvaluatorKind = Enum.Parse<EvaluatorKind>(evaluationSeed.EvaluatorKind, true),
                        EvaluatorRef = evaluationSeed.EvaluatorRef,
                        Notes = evaluationSeed.Notes,
                        Passed = evaluationSeed.Passed,
                    }, (evaluationSeed.Metrics ?? []).Select(metric => new EvaluationMetric
                    {
                        MetricId = metric.MetricId,
                        EvaluationId = evaluationSeed.EvaluationId,
                        Name = metric.Name,
                        Value = metric.Value,
                        Target = metric.Target,
                        Achieved = metric.Achieved,
                    }).ToArray(), ct);
                }
            }
        }

        foreach (var seed in request.Triggers ?? [])
        {
            await triggers.CreateAsync(new TriggerDefinition
            {
                TriggerId = seed.TriggerId,
                Name = seed.Name,
                Kind = Enum.Parse<TriggerKind>(seed.Kind, true),
                TargetKind = seed.TargetKind,
                TargetName = seed.TargetName,
                Input = seed.Input,
                Cron = seed.Cron,
                IntervalSeconds = seed.IntervalSeconds,
                OverlapPolicy = Enum.Parse<OverlapPolicy>(seed.OverlapPolicy, true),
                Enabled = seed.Enabled,
                SecretRef = seed.SecretRef,
                NextRunAt = DateTimeOffset.UtcNow.AddHours(1),
            }, ct);
        }

        foreach (var seed in request.Approvals ?? [])
        {
            var approval = ApprovalRequest.Create(seed.RunId, seed.Tool, seed.ArgsSummary, TimeSpan.FromSeconds(seed.TimeoutSeconds), approvalId: seed.ApprovalId) with
            {
                MissionId = seed.MissionId ?? request.Mission?.MissionId,
                AgentInstanceId = seed.AgentInstanceId,
                NodeRunId = seed.NodeRunId,
                IterationId = seed.IterationId,
            };
            await approvals.CreateAsync(approval, ct);
        }

        foreach (var seed in request.Artifacts ?? [])
        {
            await artifacts.SaveMissionArtifactAsync(new MissionArtifact
            {
                ArtifactId = seed.ArtifactId,
                MissionId = seed.MissionId ?? request.Mission?.MissionId ?? throw new ArgumentException("artifact missionId is required"),
                SourceMessageId = seed.SourceMessageId,
                IterationId = seed.IterationId,
                Path = seed.Path,
                Summary = seed.Summary,
                ContentHash = seed.ContentHash,
            }, ct);
        }

        return Results.Ok(new { missionId = request.Mission?.MissionId });
    }
    catch (Exception ex) when (ex is ArgumentException or FormatException or InvalidOperationException)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
}

app.Run();

public sealed record E2eApprovalSeedRequest(
    string? RunId,
    string? Tool,
    string? ArgsSummary,
    int TimeoutSeconds)
{
    public bool IsValid => IsValidText(RunId) && IsValidText(Tool) && IsValidText(ArgsSummary)
        && TimeoutSeconds is > 0 and <= 3600;

    private static bool IsValidText(string? value)
        => !string.IsNullOrWhiteSpace(value) && value.Trim().Length <= 256;
}

public sealed record MissionMessageSubmission(string Body, string? TargetInstanceId = null);

public sealed record E2eOrchestrationSeedRequest(
    E2eMissionSeed? Mission = null,
    IReadOnlyList<E2eAgentSeed>? Agents = null,
    IReadOnlyList<E2eMessageSeed>? Messages = null,
    IReadOnlyList<E2eLoopSeed>? Loops = null,
    IReadOnlyList<E2eTriggerSeed>? Triggers = null,
    IReadOnlyList<E2eApprovalSeed>? Approvals = null,
    IReadOnlyList<E2eArtifactSeed>? Artifacts = null);

public sealed record E2eMissionSeed(
    string MissionId,
    string Goal,
    string TargetKind = "Team",
    string TargetName = "demo-team",
    string? TeamName = "demo-team",
    string Status = "Running",
    string? Outcome = null,
    string? StopReason = null,
    string TriggerKind = "Manual",
    E2eBudgetSeed? Budget = null);

public sealed record E2eBudgetSeed(
    double? CostLimitUsd = 5,
    int? TimeLimitSeconds = 5400,
    int? MaxIterations = 10,
    int? MaxConcurrentAgents = 4,
    double CostUsedUsd = 0,
    int ElapsedSeconds = 0,
    int IterationsUsed = 0,
    int PeakConcurrentAgents = 0);

public sealed record E2eAgentSeed(
    string InstanceId,
    string AgentName,
    string Role = "Member",
    int InstanceNo = 1,
    string State = "Idle",
    string? MissionId = null,
    string? AwaitingInstanceId = null,
    string? ModelName = "gpt-4.1");

public sealed record E2eMessageSeed(
    string Body,
    string Kind = "Report",
    string SenderKind = "Agent",
    string? MissionId = null,
    string? MessageId = null,
    string? SenderInstanceId = null,
    string? RecipientInstanceId = null,
    int DelegationDepth = 0,
    string? InputRefs = null,
    string? CostRecordId = null,
    int SecondsAgo = 0);

public sealed record E2eLoopSeed(
    string LoopRunId,
    string NodeRunId = "review-loop",
    string? MissionId = null,
    int MaxIterations = 10,
    double? CostLimitUsd = 5,
    int? TimeLimitSeconds = 5400,
    double? ScoreThreshold = 1,
    IReadOnlyList<E2eIterationSeed>? Iterations = null);

public sealed record E2eIterationSeed(
    string IterationId,
    int IterationNo,
    string State = "Failed",
    string? InputJson = null,
    string? OutputJson = null,
    double CostUsd = 0,
    long Tokens = 0,
    long DurationMs = 0,
    E2eEvaluationSeed? Evaluation = null);

public sealed record E2eEvaluationSeed(
    string EvaluationId,
    double Score,
    string EvaluatorKind = "Deterministic",
    string EvaluatorRef = "test-result",
    string? Notes = null,
    bool Passed = false,
    IReadOnlyList<E2eMetricSeed>? Metrics = null);

public sealed record E2eMetricSeed(string MetricId, string Name, double Value, double Target, bool Achieved);

public sealed record E2eTriggerSeed(
    string TriggerId,
    string Name,
    string Kind = "Schedule",
    string TargetKind = "Team",
    string TargetName = "demo-team",
    string Input = "scheduled mission",
    string? Cron = "0 9 * * 1",
    int? IntervalSeconds = null,
    string OverlapPolicy = "Skip",
    bool Enabled = true,
    string? SecretRef = null);

public sealed record E2eApprovalSeed(
    string ApprovalId,
    string RunId,
    string Tool,
    string ArgsSummary,
    int TimeoutSeconds = 300,
    string? MissionId = null,
    string? AgentInstanceId = null,
    string? NodeRunId = null,
    string? IterationId = null);

public sealed record E2eArtifactSeed(
    string ArtifactId,
    string Path,
    string Summary,
    string ContentHash,
    string SourceMessageId = "seed-message",
    string? MissionId = null,
    string? IterationId = null);
