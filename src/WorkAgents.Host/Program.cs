using System.Text.Json.Serialization;
using WorkAgents.Agents;
using WorkAgents.Agents.DependencyInjection;
using WorkAgents.Agents.Loading;
using WorkAgents.Core;
using WorkAgents.Core.Abstractions;
using WorkAgents.Core.Missions;
using WorkAgents.Core.Teams;
using WorkAgents.Core.Graphs;
using WorkAgents.Core.Triggers;
using WorkAgents.Infrastructure.Execution;
using WorkAgents.Infrastructure.DependencyInjection;
using WorkAgents.Infrastructure.Telemetry;
using WorkAgents.Host;
using WorkAgents.Orchestration;
using WorkAgents.Orchestration.Teams;
using WorkAgents.Orchestration.Graph;
using WorkAgents.Orchestration.Replay;
using WorkAgents.Orchestration.Migration;
using WorkAgents.Host.Mcp;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddWorkAgentsInfrastructure(builder.Configuration);
builder.Services.AddWorkAgentsTelemetry(builder.Configuration, "WorkAgents.Host");
builder.Services.AddWorkAgentsAgents(builder.Configuration);
builder.Services.AddWorkAgentsMcp(builder.Configuration);
builder.Services.ConfigureHttpJsonOptions(options =>
	options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddSignalR();
builder.Services.AddSingleton<MissionHubPublisher>();
builder.Services.AddSingleton<IRunProgressPublisher, SignalRRunProgressPublisher>();
// Streaming:Enabled が false のときは sink を登録しないため、TeamExecutor は従来の一括経路で動く。
if (builder.Configuration.GetValue("Streaming:Enabled", true))
{
	builder.Services.AddSingleton<IAgentStreamSink, MissionStreamPublisher>();
}
builder.Services.AddHostedService<RunRecoveryHostedService>();
builder.Services.AddHostedService<RunBackgroundService>();
builder.Services.AddHostedService<SchedulingBackgroundService>();

var app = builder.Build();
_ = app.Services.GetRequiredService<MissionHubPublisher>();
app.UseWorkAgentsMcpSecurity();
app.MapWorkAgentsMcp();

app.MapGet("/", () => $"WorkAgents.Host running (Profile={builder.Configuration["Profile"] ?? "Local"})");
app.MapHub<RunProgressHub>("/hubs/runs");
app.MapHub<MissionHub>("/hubs/missions");

app.MapPost("/missions", async (
	MissionSubmission request,
	MissionEngine engine,
	IAgentRegistry agents,
	IReadOnlyList<TeamDefinition> teams,
	IReadOnlyList<GraphDefinition> graphs,
	ILlmModelStore modelStore,
	CancellationToken ct) =>
{
	if (string.IsNullOrWhiteSpace(request.Goal))
	{
		return Results.BadRequest(new { error = new { code = "validation_failed", message = "goal is required." } });
	}

	if (!Enum.TryParse<MissionTargetKind>(request.TargetKind, ignoreCase: true, out var targetKind))
	{
		return Results.BadRequest(new { error = new { code = "unknown_target", message = $"unknown targetKind: '{request.TargetKind}'." } });
	}

	if (string.IsNullOrWhiteSpace(request.TargetName))
	{
		return Results.BadRequest(new { error = new { code = "unknown_target", message = "targetName is required." } });
	}

	string? teamName = null;
	if (targetKind == MissionTargetKind.Team)
	{
		var team = teams.FirstOrDefault(t => string.Equals(t.Name, request.TargetName, StringComparison.Ordinal));
		if (team is null)
		{
			return Results.BadRequest(new { error = new { code = "unknown_target", message = $"unknown team: '{request.TargetName}'." } });
		}

		teamName = team.Name;
		var orchestratorModel = await modelStore.ResolveForAgentAsync(team.Orchestrator.Agent, ct);
		if (orchestratorModel is null)
		{
			return Results.BadRequest(new { error = new { code = "model_not_configured", message = $"no model configured for agent '{team.Orchestrator.Agent}'." } });
		}
	}
	else
	{
		var graphDefinition = graphs.FirstOrDefault(g => string.Equals(g.Name, request.TargetName, StringComparison.Ordinal));
		if (graphDefinition is null)
		{
			return Results.BadRequest(new { error = new { code = "unknown_target", message = $"unknown graph: '{request.TargetName}'." } });
		}
		try
		{
			var knownAgents = agents.ListAgents().Select(agent => agent.Name).ToArray();
			var knownTeams = teams.Select(team => team.Name).ToArray();
			var graph = new FileBasedGraphLoader().Load(graphDefinition.FolderPath, knownAgents, knownTeams);
			var validation = new GraphValidator().Validate(graph);
			if (!validation.IsValid)
			{
				return Results.Conflict(new { error = new { code = "graph_invalid", message = string.Join(", ", validation.Errors.Select(error => error.Code)) } });
			}
		}
		catch (GraphYamlValidationException ex)
		{
			return Results.Conflict(new { error = new { code = "graph_invalid", message = ex.Message } });
		}
	}

	var mission = new Mission
	{
		MissionId = Guid.NewGuid().ToString("N"),
		Goal = request.Goal,
		TargetKind = targetKind,
		TargetName = request.TargetName,
		TeamName = teamName,
		TriggerKind = MissionTriggerKind.Manual,
	};
	if (request.Budget is not null)
	{
		await engine.ConfigureBudgetAsync(mission.MissionId, request.Budget.ToBudget(mission.MissionId), ct);
	}

	var created = await engine.SubmitAsync(mission, ct);
	return Results.Created($"/missions/{created.MissionId}", new MissionAccepted(
		created.MissionId, created.Status, created.QueuedReason?.ToString(), created.QueuePosition));
});

app.MapGet("/missions", async (
	string? outcome,
	string? status,
	string? team,
	DateTimeOffset? from,
	DateTimeOffset? to,
	int? limit,
	int? offset,
	IMissionStore store,
	CancellationToken ct) =>
{
	var query = new MissionQuery
	{
		Outcomes = ParseEnums<MissionOutcome>(outcome),
		Statuses = ParseEnums<MissionStatus>(status),
		TeamName = team,
		From = from,
		To = to,
		Limit = Math.Clamp(limit ?? 50, 1, 500),
		Offset = Math.Max(0, offset ?? 0),
	};
	return Results.Ok(await store.ListAsync(query, ct));
});

app.MapGet("/missions/{missionId}", async (string missionId, IMissionStore store, CancellationToken ct) =>
{
	var mission = await store.GetAsync(missionId, ct);
	return mission is null
		? Results.NotFound(new { error = new { code = "mission_not_found", message = $"mission not found: '{missionId}'." } })
		: Results.Ok(mission);
});

app.MapGet("/missions/{missionId}/loops", async (string missionId, ILoopStore loops, CancellationToken ct) =>
{
	var runs = await loops.ListLoopRunsAsync(missionId, ct);
	var response = new List<object>();
	foreach (var run in runs)
	{
		var iterations = await loops.ListIterationsAsync(run.LoopRunId, ct: ct);
		var iterationResponse = new List<object>();
		var blocking = new List<string>();
		foreach (var iteration in iterations)
		{
			var evaluation = await loops.GetEvaluationAsync(iteration.IterationId, ct);
			var metrics = evaluation is null ? Array.Empty<object>() : (await loops.ListMetricsAsync(evaluation.EvaluationId, ct)).Select(metric => (object)new
			{
				name = metric.Name,
				value = metric.Value,
				target = metric.Target,
				achieved = metric.Achieved,
			}).ToArray();
			if (evaluation is not null)
			{
				foreach (var metric in await loops.ListMetricsAsync(evaluation.EvaluationId, ct))
				{
					if (!metric.Achieved) blocking.Add(metric.Name);
				}
			}
			iterationResponse.Add(new
			{
				iterationNo = iteration.IterationNo,
				score = evaluation?.Score,
				passed = evaluation?.Passed ?? false,
				metrics,
				notes = evaluation?.Notes,
				costUsd = iteration.CostUsd,
			});
		}
		response.Add(new
		{
			loopRunId = run.LoopRunId,
			currentIteration = iterations.LastOrDefault()?.IterationNo ?? 0,
			maxIterations = run.MaxIterations,
			stopReason = run.StopReason,
			bestIterationId = run.BestIterationId,
			iterations = iterationResponse,
			blockingMetrics = blocking.Distinct(StringComparer.Ordinal).ToArray(),
		});
	}
	return Results.Ok(new { loopRuns = response });
});

app.MapPost("/missions/{missionId}/loops/{loopRunId}/break", async (string missionId, string loopRunId, ILoopStore loops, CancellationToken ct) =>
{
	var run = await loops.GetLoopRunAsync(loopRunId, ct);
	if (run is null || run.MissionId != missionId) return Results.NotFound();
	await loops.CompleteLoopRunAsync(loopRunId, WorkAgents.Core.Loops.LoopStopReason.UserBreak, run.BestIterationId, ct);
	return Results.Ok(new { bestIterationId = run.BestIterationId });
});

app.MapGet("/missions/{missionId}/graph", async (string missionId, IGraphVersionStore graphs, CancellationToken ct) =>
	Results.Ok(new
	{
		nodes = await graphs.ListNodeRunsAsync(missionId, ct),
		edges = await graphs.ListEdgeTransitsAsync(missionId, ct),
	}));

app.MapGet("/missions/{missionId}/costs", async (string missionId, MissionReportBuilder reports, CancellationToken ct)
	=> Results.Ok(await reports.BuildAsync(missionId, ct: ct)));

app.MapGet("/missions/{missionId}/artifacts", async (string missionId, bool? includeDiscarded, IMissionArtifactStore artifacts, CancellationToken ct)
	=> Results.Ok(await artifacts.ListMissionAsync(missionId, includeDiscarded ?? false, ct)));

app.MapGet("/missions/{missionId}/artifacts/{artifactId}/content", async (string missionId, string artifactId, WorkAgents.Infrastructure.Stores.ArtifactDownloadResolver resolver, CancellationToken ct) =>
	{
		var resolved = await resolver.ResolveAsync(missionId, artifactId, ct);
		return resolved switch
		{
			WorkAgents.Infrastructure.Stores.ArtifactDownloadResult.Found found => Results.Stream(found.Content, found.ContentType, found.FileName),
			_ => Results.NotFound(new { error = new { code = "artifact_not_found", message = "artifact not found or unavailable" } }),
		};
	});

app.MapGet("/missions/{missionId}/workspace/files", async (
	string missionId,
	IMissionWorkspaceReader reader,
	ILoggerFactory loggerFactory,
	CancellationToken ct) =>
{
	try
	{
		var snapshot = await reader.ReadAsync(missionId, ct);
		return Results.Ok(new
		{
			missionId = snapshot.MissionId,
			state = snapshot.State,
			observedAtUtc = snapshot.ObservedAtUtc,
			items = snapshot.Items.Select(item => new
			{
				path = item.RelativePath,
				kind = item.Kind,
				sizeBytes = item.SizeBytes,
				lastWriteTimeUtc = item.LastWriteTimeUtc,
				status = item.Status,
			}).ToArray(),
		});
	}
	catch (KeyNotFoundException)
	{
		return Results.NotFound(new { error = new { code = "mission_not_found", message = "mission not found or unavailable" } });
	}
	catch (ArgumentException)
	{
		return Results.BadRequest(new { error = new { code = "validation_failed", message = "mission ID is invalid" } });
	}
	catch (OperationCanceledException) when (ct.IsCancellationRequested)
	{
		throw;
	}
	catch (Exception ex)
	{
		loggerFactory.CreateLogger("MissionWorkspaceEndpoint").LogError(ex, "mission workspace endpoint failed mission={MissionId}", missionId);
		return Results.Json(
			new { error = new { code = "workspace_unavailable", message = "workspace is unavailable" } },
			statusCode: StatusCodes.Status503ServiceUnavailable);
	}
});

app.MapGet("/workspace/usage", (WorkspaceUsageReportBuilder reports) => Results.Ok(reports.Build()));

app.MapGet("/teams", (IReadOnlyList<TeamDefinition> teams) => Results.Ok(teams));
app.MapGet("/teams/{name}", (string name, IReadOnlyList<TeamDefinition> teams) =>
	{
		var team = teams.FirstOrDefault(candidate => string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase));
		return team is null ? Results.NotFound() : Results.Ok(team);
	});

app.MapGet("/graphs", (IReadOnlyList<GraphDefinition> graphs) => Results.Ok(graphs.Select(graph => new { name = graph.Name, displayName = graph.DisplayName, nodes = graph.Nodes.Count, sourceLabel = graph.SourceLabel })));
app.MapGet("/graphs/{name}", (string name) =>
	{
		var folder = Path.Combine(FileBasedGraphLoader.ResolveGraphsRoot(AppContext.BaseDirectory), name);
		if (!File.Exists(Path.Combine(folder, "graph.yaml"))) return Results.NotFound();
		return Results.Ok(new { name, yaml = File.ReadAllText(Path.Combine(folder, "graph.yaml")) });
	});
app.MapPost("/graphs/{name}/validate", (string name, GraphValidateRequest request, FileBasedGraphLoader loader, GraphValidator validator) =>
	{
		try
		{
			var graph = loader.LoadText(request.Yaml, Path.Combine(FileBasedGraphLoader.ResolveGraphsRoot(AppContext.BaseDirectory), name));
			var result = validator.Validate(graph);
			return result.IsValid
				? Results.Ok(new { valid = true, contentHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(request.Yaml))).ToLowerInvariant()[..16] })
				: Results.UnprocessableEntity(new { valid = false, errors = result.Errors });
		}
		catch (Exception ex) when (ex is GraphYamlValidationException or FormatException)
		{
			return Results.UnprocessableEntity(new { valid = false, errors = new[] { new { code = "validation_failed", message = ex.Message } } });
		}
	});
app.MapPut("/graphs/{name}", async (string name, GraphValidateRequest request, FileBasedGraphLoader loader, GraphValidator validator, GraphYamlWriter writer, CancellationToken ct) =>
	{
		var folder = Path.Combine(FileBasedGraphLoader.ResolveGraphsRoot(AppContext.BaseDirectory), name);
		try
		{
			var graph = loader.LoadText(request.Yaml, folder);
			var result = validator.Validate(graph);
			if (!result.IsValid) return Results.UnprocessableEntity(new { valid = false, errors = result.Errors });
			await writer.WriteAsync(graph, Path.Combine(folder, "graph.yaml"), ct);
			return Results.Ok(new { valid = true, name });
		}
		catch (Exception ex) when (ex is GraphYamlValidationException or FormatException)
		{
			return Results.UnprocessableEntity(new { valid = false, errors = new[] { new { code = "validation_failed", message = ex.Message } } });
		}
	});

app.MapGet("/triggers", async (ITriggerStore triggers, CancellationToken ct) => Results.Ok(await triggers.ListAsync(ct)));
app.MapGet("/triggers/{name}", async (string name, ITriggerStore triggers, CancellationToken ct) =>
	{
		var trigger = await triggers.GetAsync(name, ct);
		return trigger is null ? Results.NotFound() : Results.Ok(trigger);
	});
app.MapPost("/triggers", async (TriggerSubmission request, ITriggerStore triggers, CancellationToken ct) =>
{
	var now = DateTimeOffset.UtcNow;
	var trigger = request.ToDefinition(Guid.NewGuid().ToString("N"), now);
	trigger = trigger with { NextRunAt = TriggerScheduleCalculator.GetNextOccurrence(trigger, now) };
	await triggers.CreateAsync(trigger, ct);
	return Results.Created($"/triggers/{trigger.Name}", trigger);
});
app.MapPut("/triggers/{name}", async (string name, TriggerSubmission request, ITriggerStore triggers, CancellationToken ct) =>
{
	var existing = await triggers.GetAsync(name, ct);
	if (existing is null) return Results.NotFound();
	var trigger = request.ToDefinition(existing.TriggerId, existing.CreatedAt) with { Name = name };
	trigger = trigger with { NextRunAt = TriggerScheduleCalculator.GetNextOccurrence(trigger, DateTimeOffset.UtcNow) };
	await triggers.UpdateAsync(trigger, ct);
	return Results.Ok(trigger);
});
app.MapDelete("/triggers/{name}", async (string name, ITriggerStore triggers, CancellationToken ct) =>
{
	await triggers.DeleteAsync(name, ct);
	return Results.NoContent();
});
app.MapPost("/triggers/{name}/enable", async (string name, ITriggerStore triggers, CancellationToken ct) => { await triggers.SetEnabledAsync(name, true, ct); return Results.Ok(); });
app.MapPost("/triggers/{name}/disable", async (string name, ITriggerStore triggers, CancellationToken ct) => { await triggers.SetEnabledAsync(name, false, ct); return Results.Ok(); });
app.MapGet("/triggers/{name}/fires", async (string name, ITriggerStore triggers, CancellationToken ct) =>
{
	var trigger = await triggers.GetAsync(name, ct);
	return trigger is null ? Results.NotFound() : Results.Ok(await triggers.ListFiresAsync(trigger.TriggerId, ct));
});
app.MapPost("/triggers/{name}/events", async (HttpContext httpContext, string name, TriggerEventSubmission request, ITriggerStore triggers, ISecretStore secrets, MissionEngine engine, CancellationToken ct) =>
{
	var trigger = await triggers.GetAsync(name, ct);
	if (trigger is null) return Results.NotFound();
	if (trigger.Kind != TriggerKind.Event || trigger.SecretRef is null) return Results.Unauthorized();
	if (httpContext.Connection.RemoteIpAddress is not null && !System.Net.IPAddress.IsLoopback(httpContext.Connection.RemoteIpAddress)) return Results.Unauthorized();
	var expected = await secrets.GetAsync(trigger.SecretRef, ct);
	var actual = httpContext.Request.Headers["X-WorkAgents-Trigger-Token"].ToString();
	if (string.IsNullOrEmpty(expected) || !string.Equals(expected, actual, StringComparison.Ordinal)) return Results.Unauthorized();
	var mission = await engine.SubmitAsync(new Mission
	{
		MissionId = Guid.NewGuid().ToString("N"),
		Goal = string.IsNullOrWhiteSpace(request.Input) ? trigger.Input : request.Input,
		TargetKind = Enum.TryParse<MissionTargetKind>(trigger.TargetKind, true, out var target) ? target : MissionTargetKind.Team,
		TargetName = trigger.TargetName,
		TeamName = trigger.TargetKind,
		TriggerId = trigger.TriggerId,
		TriggerKind = MissionTriggerKind.Event,
	}, ct);
	return Results.Accepted(null, new { missionId = mission.MissionId, decision = mission.Status.ToString().ToLowerInvariant() });
});

app.MapPost("/migrations/workflows", async (MigrationRequest request, IWorkflowRegistry workflows, GraphYamlWriter writer, CancellationToken ct) =>
{
	var converter = new WorkflowToGraphConverter();
	var selected = workflows.ListWorkflows()
		.Where(workflow => request.Names is null || request.Names.Contains(workflow.Name, StringComparer.OrdinalIgnoreCase))
		.Select(workflow => workflows.GetWorkflow(workflow.Name))
		.Where(workflow => workflow is not null)
		.Select(workflow => workflow!)
		.ToArray();
	var results = new List<object>();
	foreach (var workflow in selected)
	{
		var conversion = converter.Convert(workflow);
		var yaml = writer.ToYaml(conversion.Graph);
		if (!request.DryRun)
		{
			var graphsRoot = FileBasedGraphLoader.ResolveGraphsRoot(AppContext.BaseDirectory);
			await writer.WriteAsync(conversion.Graph, Path.Combine(graphsRoot, workflow.Name, "graph.yaml"), ct);
		}
		results.Add(new { name = workflow.Name, yaml, topologicalOrder = conversion.TopologicalOrder, scheduleCron = conversion.ScheduleCron });
	}
	return Results.Ok(new { dryRun = request.DryRun, workflows = results });
});

if (args.Any(arg => string.Equals(arg, "migrate-workflows", StringComparison.OrdinalIgnoreCase)))
{
	var dryRun = args.Any(arg => string.Equals(arg, "--dry-run", StringComparison.OrdinalIgnoreCase));
	var registry = app.Services.GetRequiredService<IWorkflowRegistry>();
	var writer = app.Services.GetRequiredService<GraphYamlWriter>();
	var converter = new WorkflowToGraphConverter();
	foreach (var workflowView in registry.ListWorkflows())
	{
		var workflow = registry.GetWorkflow(workflowView.Name);
		if (workflow is null) continue;
		var conversion = converter.Convert(workflow);
		Console.WriteLine(writer.ToYaml(conversion.Graph));
		if (!dryRun)
		{
			var graphsRoot = FileBasedGraphLoader.ResolveGraphsRoot(AppContext.BaseDirectory);
			await writer.WriteAsync(conversion.Graph, Path.Combine(graphsRoot, workflow.Name, "graph.yaml"));
		}
	}
	return;
}

app.MapGet("/missions/{missionId}/messages", async (
	string missionId,
	long? sinceSeq,
	string? threadKey,
	bool? includeDiscarded,
	int? limit,
	IMessageStore store,
	CancellationToken ct) =>
	Results.Ok(await store.ListAsync(missionId, sinceSeq ?? 0, threadKey, includeDiscarded ?? false, Math.Clamp(limit ?? 500, 1, 2_000), ct)));

app.MapGet("/missions/{missionId}/agents", async (string missionId, IAgentInstanceStore store, CancellationToken ct) =>
	Results.Ok(await store.ListByMissionAsync(missionId, ct)));

app.MapPost("/missions/{missionId}/messages", async (
	string missionId,
	MissionMessageSubmission request,
	IMissionStore missionStore,
	IMessageStore messageStore,
	IInterventionStore interventionStore,
	MessageBus bus,
	MissionEngine engine,
	CancellationToken ct) =>
{
	if (string.IsNullOrWhiteSpace(request.Body))
	{
		return Results.BadRequest(new { error = new { code = "validation_failed", message = "body is required." } });
	}
	var mission = await missionStore.GetAsync(missionId, ct);
	if (mission is null)
	{
		return Results.NotFound(new { error = new { code = "mission_not_found", message = "mission not found." } });
	}
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
	await engine.TryResumeFromInterventionAsync(missionId, ct);
	return Results.Accepted($"/missions/{missionId}/messages", new { interventionId = intervention.InterventionId, messageId = message.MessageId, seq = message.Seq });
});

app.MapPost("/missions/{missionId}/pause", async (string missionId, MissionEngine engine, CancellationToken ct)
	=> await ExecuteMissionCommandAsync(() => engine.PauseAsync(missionId, ct)));
app.MapPost("/missions/{missionId}/resume", async (string missionId, MissionEngine engine, CancellationToken ct)
	=> await ExecuteMissionCommandAsync(() => engine.ResumeAsync(missionId, ct)));
app.MapPost("/missions/{missionId}/abort", async (string missionId, MissionEngine engine, CancellationToken ct)
	=> await ExecuteMissionCommandAsync(() => engine.AbortAsync(missionId, ct)));
app.MapPost("/missions/{missionId}/agents/{instanceId}/stop", async (string missionId, string instanceId, MissionEngine engine, CancellationToken ct)
	=> await ExecuteMissionCommandAsync(() => engine.StopAgentAsync(missionId, instanceId, ct)));

app.MapPost("/runs", async (
	RunSubmission request,
	IAgentRegistry agents,
	IRunStore store,
	IRunQueue queue,
	CancellationToken ct) =>
{
	if (string.IsNullOrWhiteSpace(request.AgentName) || string.IsNullOrWhiteSpace(request.UserMessage))
	{
		return Results.BadRequest(new { error = "agentName and userMessage are required." });
	}

	if (!agents.ListAgents().Any(agent => string.Equals(agent.Name, request.AgentName, StringComparison.OrdinalIgnoreCase)))
	{
		return Results.BadRequest(new { error = $"agent not found: '{request.AgentName}'." });
	}

	var runId = Guid.NewGuid().ToString("N");
	var threadId = string.IsNullOrWhiteSpace(request.ThreadId)
		? Guid.NewGuid().ToString("N")
		: request.ThreadId.Trim();
	var runRequest = new RunRequest(request.AgentName, request.UserMessage, threadId);
	var run = new RunRecord
	{
		RunId = runId,
		AgentName = runRequest.AgentName,
		UserMessage = runRequest.UserMessage,
		ThreadId = runRequest.ThreadId,
	};

	await store.CreateAsync(run, ct);
	try
	{
		await queue.EnqueueAsync(runId, ct);
	}
	catch
	{
		await store.CompleteAsync(
			runId,
			RunStatus.Aborted,
			error: "Run could not be queued.",
			ct: CancellationToken.None);
		throw;
	}

	return Results.Accepted($"/runs/{runId}", new RunAccepted(runId, run.Status, run.ThreadId!));
});

app.MapGet("/runs", async (IRunStore store, CancellationToken ct) =>
	Results.Ok(await store.ListAsync(ct)));

app.MapGet("/runs/{runId}", async (string runId, IRunStore store, CancellationToken ct) =>
{
	var run = await store.GetAsync(runId, ct);
	return run is null ? Results.NotFound() : Results.Ok(run);
});

app.MapPost("/runs/{runId}/cancel", async (
	string runId,
	IRunStore store,
	IRunCancellationRegistry cancellationRegistry,
	CancellationToken ct) =>
{
	var run = await store.GetAsync(runId, ct);
	if (run is null)
	{
		return Results.NotFound();
	}

	if (run.Status is RunStatus.Succeeded or RunStatus.Failed or RunStatus.Aborted)
	{
		return Results.Conflict(new { error = $"run already finished with status '{run.Status}'." });
	}

	if (cancellationRegistry.TryCancel(runId))
	{
		return Results.Accepted($"/runs/{runId}");
	}

	// Not yet claimed by the background service (still Queued): abort it directly.
	try
	{
		await store.CompleteAsync(runId, RunStatus.Aborted, error: "Run was cancelled before it started.", ct: ct);
		return Results.Accepted($"/runs/{runId}");
	}
	catch (InvalidOperationException)
	{
		// Claimed concurrently between our read and the abort attempt: retry via the registry once.
		return cancellationRegistry.TryCancel(runId)
			? Results.Accepted($"/runs/{runId}")
			: Results.Conflict(new { error = "run could not be cancelled; it may have just finished." });
	}
});

app.MapGet("/approvals", async (string? runId, string? missionId, IApprovalStore store, CancellationToken ct) =>
	Results.Ok((await store.ListPendingAsync(runId, ct)).Where(request => missionId is null || request.MissionId == missionId)));

app.MapGet("/approvals/{approvalId}", async (string approvalId, IApprovalStore store, CancellationToken ct) =>
{
	var approval = await store.GetAsync(approvalId, ct);
	return approval is null ? Results.NotFound() : Results.Ok(approval);
});

app.MapPost("/approvals/{approvalId}/decide", async (
	string approvalId,
	ApprovalDecisionRequest request,
	IApprovalStore approvalStore,
	IApprovalService approvalService,
	CancellationToken ct) =>
{
	if (string.IsNullOrWhiteSpace(request.DecidedBy))
	{
		return Results.BadRequest(new { error = "decidedBy is required." });
	}

	if (request.Status is not (ApprovalStatus.Approved or ApprovalStatus.Rejected))
	{
		return Results.BadRequest(new { error = "status must be 'Approved' or 'Rejected'." });
	}

	var existing = await approvalStore.GetAsync(approvalId, ct);
	if (existing is null)
	{
		return Results.NotFound();
	}

	var decided = await approvalService.DecideAsync(approvalId, request.Status, request.DecidedBy, request.Reason, ct);
	return decided
		? Results.Ok()
		: Results.Conflict(new { error = "approval is no longer pending (already decided or expired)." });
});

static IReadOnlyList<T>? ParseEnums<T>(string? value) where T : struct, Enum
{
	if (string.IsNullOrWhiteSpace(value))
	{
		return null;
	}
	var parsed = new List<T>();
	foreach (var item in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
	{
		if (!Enum.TryParse<T>(item, true, out var result))
		{
			return Array.Empty<T>();
		}
		parsed.Add(result);
	}
	return parsed;
}

static async Task<IResult> ExecuteMissionCommandAsync(Func<Task> command)
{
	try
	{
		await command();
		return Results.Accepted();
	}
	catch (KeyNotFoundException)
	{
		return Results.NotFound(new { error = new { code = "mission_not_found", message = "mission not found." } });
	}
	catch (InvalidOperationException ex)
	{
		return Results.Conflict(new { error = new { code = "invalid_transition", message = ex.Message } });
	}
}

app.Run();

public sealed record RunSubmission(string AgentName, string UserMessage, string? ThreadId = null);

public sealed record RunAccepted(string RunId, RunStatus Status, string ThreadId);

public sealed record ApprovalDecisionRequest(ApprovalStatus Status, string DecidedBy, string? Reason = null);

public sealed record MissionSubmission(string Goal, string TargetKind, string TargetName, MissionBudgetSubmission? Budget = null);

public sealed record MissionBudgetSubmission(
	double? CostLimitUsd = null,
	int? TimeLimitSeconds = null,
	int? MaxIterations = null,
	int? MaxConcurrentAgents = null)
{
	public Budget ToBudget(string missionId) => new()
	{
		MissionId = missionId,
		CostLimitUsd = CostLimitUsd,
		TimeLimitSeconds = TimeLimitSeconds,
		MaxIterations = MaxIterations,
		MaxConcurrentAgents = MaxConcurrentAgents,
	};
}

public sealed record MissionMessageSubmission(string Body, string? TargetInstanceId = null);

public sealed record GraphValidateRequest(string Yaml);

public sealed record TriggerEventSubmission(string? Input = null);

public sealed record MigrationRequest(bool DryRun = true, IReadOnlyList<string>? Names = null);

public sealed record TriggerSubmission(
	string Name,
	string Kind,
	string TargetKind,
	string TargetName,
	string Input,
	string? Cron = null,
	int? IntervalSeconds = null,
	string OverlapPolicy = "skip",
	bool Enabled = true,
	string? SecretRef = null)
{
	public TriggerDefinition ToDefinition(string triggerId, DateTimeOffset createdAt) => new()
	{
		TriggerId = triggerId,
		Name = Name,
		Kind = Enum.TryParse<TriggerKind>(Kind, true, out var kind) ? kind : throw new ArgumentException("Unknown trigger kind."),
		TargetKind = TargetKind,
		TargetName = TargetName,
		Input = Input,
		Cron = Cron,
		IntervalSeconds = IntervalSeconds,
		OverlapPolicy = Enum.TryParse<OverlapPolicy>(OverlapPolicy, true, out var policy) ? policy : throw new ArgumentException("Unknown overlap policy."),
		Enabled = Enabled,
		SecretRef = SecretRef,
		NextRunAt = null,
		CreatedAt = createdAt,
		UpdatedAt = DateTimeOffset.UtcNow,
	};
}

public sealed record MissionAccepted(string MissionId, MissionStatus Status, string? QueuedReason, int? QueuePosition);

public partial class Program { }
