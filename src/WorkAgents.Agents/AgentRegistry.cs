using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using WorkAgents.Agents.Loading;
using WorkAgents.Core;
using WorkAgents.Core.Abstractions;
using WorkAgents.Harness.Harness;
using WorkAgents.Agents.Tools;

namespace WorkAgents.Agents;

/// <summary>
/// <see cref="IAgentRegistry"/> の M1 実装。起動時に読み込んだ <see cref="AgentDefinition"/> 一覧を保持し、
/// 名前で解決して <see cref="LlmAgentFactory"/> 経由で <c>AIAgent</c> を都度構築して実行する。
/// M3 で非同期ジョブ経路、M4 で承認ブリッジ、M6 でコスト middleware をここへ重ねる。
/// </summary>
public sealed class AgentRegistry : IAgentRegistry, IWorkflowRegistry
{
    private readonly Dictionary<string, AgentDefinition> _byName;
    private readonly Dictionary<string, WorkflowDefinition> _workflowsByName;
    private readonly IReadOnlyList<AgentView> _views;
    private readonly IReadOnlyList<WorkflowView> _workflowViews;
    private readonly IReadOnlyList<ToolView> _tools;
    private readonly LlmAgentFactory _factory;
    private readonly ILlmModelStore _modelStore;
    private readonly ILogger<AgentRegistry> _logger;
    private readonly HarnessApprovalBridge? _approvalBridge;
    private readonly ISessionStore? _sessionStore;
    private readonly IApprovalService? _approvalService;
    private readonly IWorkflowScriptRunner? _scriptRunner;
    private readonly AgentToolCatalog? _toolCatalog;
    private readonly ICostStore? _costStore;
    private readonly bool _deterministicE2eResponse;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _threadGates = new(StringComparer.Ordinal);
    private static readonly TimeSpan DefaultApprovalTimeout = TimeSpan.FromMinutes(15);

    public AgentRegistry(
        IReadOnlyList<AgentDefinition> definitions,
        LlmAgentFactory factory,
        ILlmModelStore modelStore,
        ILogger<AgentRegistry> logger,
        HarnessApprovalBridge? approvalBridge = null,
        ISessionStore? sessionStore = null,
        IReadOnlyList<WorkflowDefinition>? workflows = null,
        IApprovalService? approvalService = null,
        IWorkflowScriptRunner? scriptRunner = null,
        AgentToolCatalog? toolCatalog = null,
        ICostStore? costStore = null,
        bool deterministicE2eResponse = false)
    {
        _factory = factory;
        _modelStore = modelStore;
        _logger = logger;
        _approvalBridge = approvalBridge;
        _sessionStore = sessionStore;
        _approvalService = approvalService;
        _scriptRunner = scriptRunner;
        _toolCatalog = toolCatalog;
        _costStore = costStore;
        _deterministicE2eResponse = deterministicE2eResponse;
        _byName = new Dictionary<string, AgentDefinition>(StringComparer.OrdinalIgnoreCase);
        _workflowsByName = new Dictionary<string, WorkflowDefinition>(StringComparer.OrdinalIgnoreCase);
        var views = new List<AgentView>(definitions.Count);
        foreach (var d in definitions)
        {
            if (_byName.ContainsKey(d.Name))
            {
                _logger.LogWarning("duplicate agent name '{Name}' skipped (folder={Folder})", d.Name, d.FolderPath);
                continue;
            }
            _byName[d.Name] = d;
            views.Add(new AgentView(d.Name, d.DisplayName, d.Description, BuildSkillViews(d), d.SourceLabel));
        }

        if (workflows is not null)
        {
            var wfViews = new List<WorkflowView>(workflows.Count);
            foreach (var w in workflows)
            {
                if (_workflowsByName.ContainsKey(w.Name))
                {
                    _logger.LogWarning("duplicate workflow name '{Name}' skipped (folder={Folder})", w.Name, w.FolderPath);
                    continue;
                }
                if (_byName.ContainsKey(w.Name))
                {
                    _logger.LogWarning("workflow name '{Name}' collides with an agent; workflow skipped (folder={Folder})", w.Name, w.FolderPath);
                    continue;
                }
                _workflowsByName[w.Name] = w;
                wfViews.Add(new WorkflowView(
                    w.Name,
                    w.DisplayName,
                    w.Description,
                    w.Steps.Count,
                    w.ScheduleCron,
                    w.ScheduleCron is not null,
                    w.SourceLabel));
                views.Add(new AgentView(w.Name, w.DisplayName, w.Description, Array.Empty<SkillView>(), w.SourceLabel));
            }
            _workflowViews = wfViews;
        }
        else
        {
            _workflowViews = Array.Empty<WorkflowView>();
        }

        _views = views;
        _tools = BuildToolViews(_byName.Values, _toolCatalog);
    }

    public IReadOnlyList<AgentView> ListAgents() => _views;

    public IReadOnlyList<ToolView> ListTools() => _tools;

    public IReadOnlyList<WorkflowView> ListWorkflows() => _workflowViews;

    public WorkflowDefinition? GetWorkflow(string name)
        => _workflowsByName.TryGetValue(name, out var w) ? w : null;

    private static IReadOnlyList<SkillView> BuildSkillViews(AgentDefinition definition)
    {
        var skills = definition.LocalSkillNames
            .Select(name => CreateSkillView(definition, name, "local"))
            .ToList();
        skills.AddRange(definition.SharedSkillNames
            .Where(sharedName => !definition.LocalSkillNames.Contains(sharedName, StringComparer.OrdinalIgnoreCase))
            .Select(name => CreateSkillView(definition, name, "shared")));
        return skills;
    }

    private static SkillView CreateSkillView(AgentDefinition definition, string name, string source)
    {
        var skillDirectory = source == "local"
            ? Path.Combine(definition.FolderPath, "skills", name)
            : definition.SharedSkillPaths.TryGetValue(name, out var resolvedPath)
                ? resolvedPath
                : Path.Combine(definition.FolderPath, "..", "..", "skills", name);
        var skillPath = Path.GetFullPath(Path.Combine(skillDirectory, "SKILL.md"));
        var content = File.Exists(skillPath) ? File.ReadAllText(skillPath) : "";
        return new SkillView(name, source, content);
    }

    private static IReadOnlyList<ToolView> BuildToolViews(
        IEnumerable<AgentDefinition> definitions,
        AgentToolCatalog? toolCatalog)
    {
        var all = definitions.ToArray();
        var tools = new List<ToolView>();
        foreach (var definition in all)
        {
            var hasSkills = definition.LocalSkillNames.Count > 0 || definition.SharedSkillNames.Count > 0;
            var needsHarness = definition.HarnessShell
                || string.Equals(definition.HarnessFileStore, "workspace", StringComparison.OrdinalIgnoreCase)
                || hasSkills;
            if (needsHarness)
            {
                tools.AddRange(HarnessToolCatalog.List(definition.HarnessShell, hasSkills).Select(tool => new ToolView(
                    tool.Name,
                    tool.Description,
                    tool.Source,
                    tool.Approval,
                    [definition.Name])));
            }

            if (toolCatalog is not null)
            {
                tools.AddRange(toolCatalog.GetRegistrations(definition.Name).Select(registration => new ToolView(
                    registration.Name,
                    registration.Description,
                    registration.Source,
                    registration.Approval,
                    [definition.Name])));
            }
        }

        return tools
            .GroupBy(
                tool => new { tool.Name, tool.Description, tool.Source, tool.Approval },
                tool => tool.Agents.Single())
            .Select(group => new ToolView(
                group.Key.Name,
                group.Key.Description,
                group.Key.Source,
                group.Key.Approval,
                group.ToArray()))
            .OrderBy(tool => tool.Name, StringComparer.Ordinal)
            .ThenBy(tool => tool.Agents.FirstOrDefault(), StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public async Task<string> RunAsync(string agentName, string userMessage, CancellationToken cancellationToken = default)
        => await RunAsync(
            agentName,
            userMessage,
            workingDirectory: null,
            threadId: null,
            runId: null,
            cancellationToken);

    public async Task<string> RunAsync(
        string agentName,
        string userMessage,
        string? workingDirectory,
        CancellationToken cancellationToken = default)
        => await RunAsync(
            agentName,
            userMessage,
            workingDirectory,
            threadId: null,
            runId: null,
            cancellationToken);

    public async Task<string> RunAsync(
        string agentName,
        string userMessage,
        string? workingDirectory,
        string? runId,
        CancellationToken cancellationToken = default)
    {
        var def = Resolve(agentName, userMessage);
        return await RunAsync(
            def,
            userMessage,
            workingDirectory,
            threadId: null,
            runId,
            cancellationToken);
    }

    public async Task<string> RunAsync(
        string agentName,
        string userMessage,
        string? workingDirectory,
        string? threadId,
        string? runId,
        CancellationToken cancellationToken = default)
    {
        if (_workflowsByName.TryGetValue(agentName, out var workflow))
        {
            throw new WorkflowMigrationRequiredException(workflow.Name);
        }

        var def = Resolve(agentName, userMessage);
        return await RunAsync(def, userMessage, workingDirectory, threadId, runId, cancellationToken);
    }

    private async Task<string> RunAsync(
        AgentDefinition def,
        string userMessage,
        string? workingDirectory,
        string? threadId,
        string? runId,
        CancellationToken cancellationToken)
    {
        threadId = string.IsNullOrWhiteSpace(threadId) ? null : threadId.Trim();
        var gate = threadId is null
            ? null
            : _threadGates.GetOrAdd(threadId, static _ => new SemaphoreSlim(1, 1));
        if (gate is not null)
        {
            await gate.WaitAsync(cancellationToken);
        }

        try
        {
            return await RunCoreAsync(def, userMessage, workingDirectory, threadId, runId, cancellationToken);
        }
        finally
        {
            gate?.Release();
        }
    }

    private async Task<string> RunWorkflowAsync(
        WorkflowDefinition workflow,
        string userMessage,
        string? workingDirectory,
        string? runId,
        CancellationToken cancellationToken)
    {
        if (workflow.Steps.Count == 0)
        {
            throw new InvalidOperationException($"workflow '{workflow.Name}' has no steps.");
        }

        _logger.LogInformation("run workflow='{Name}' steps={Steps}", workflow.Name, workflow.Steps.Count);

        var results = new Dictionary<string, WorkflowStepResult>(StringComparer.Ordinal);
        string last = "";
        foreach (var step in workflow.Steps)
        {
            _logger.LogInformation("workflow step='{Step}' kind={Kind}", step.Name, step.Kind);
            last = step.Kind switch
            {
                WorkflowStepKind.Agent => await RunWorkflowAgentStepAsync(workflow, step, userMessage, workingDirectory, runId, results, cancellationToken),
                WorkflowStepKind.Code => await RunWorkflowCodeStepAsync(workflow, step, userMessage, results, cancellationToken),
                WorkflowStepKind.Approve => await RunWorkflowApproveStepAsync(workflow, step, userMessage, runId, results, cancellationToken),
                _ => throw new InvalidOperationException($"workflow '{workflow.Name}' step '{step.Name}' has unsupported kind '{step.Kind}'."),
            };
            _logger.LogInformation("workflow step='{Step}' completed", step.Name);
        }

        return last;
    }

    private async Task<string> RunWorkflowAgentStepAsync(
        WorkflowDefinition workflow,
        WorkflowStep step,
        string workflowInput,
        string? workingDirectory,
        string? runId,
        Dictionary<string, WorkflowStepResult> results,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(step.Agent))
        {
            throw new InvalidOperationException($"workflow '{workflow.Name}' step '{step.Name}' requires an agent.");
        }
        if (!_byName.ContainsKey(step.Agent))
        {
            throw new InvalidOperationException(
                $"workflow '{workflow.Name}' step '{step.Name}' references unknown agent '{step.Agent}'.");
        }

        var rendered = RenderTemplate(step.Input ?? "", workflowInput, results);
        var result = await RunAsync(step.Agent, rendered, workingDirectory, threadId: null, runId, cancellationToken);
        results[step.Name] = WorkflowStepResult.FromString(result);
        return result;
    }

    private async Task<string> RunWorkflowCodeStepAsync(
        WorkflowDefinition workflow,
        WorkflowStep step,
        string workflowInput,
        Dictionary<string, WorkflowStepResult> results,
        CancellationToken cancellationToken)
    {
        if (_scriptRunner is null)
        {
            throw new InvalidOperationException("workflow code step requires IWorkflowScriptRunner to be registered.");
        }

        string code;
        if (!string.IsNullOrWhiteSpace(step.CodeFile))
        {
            if (!File.Exists(step.CodeFile))
            {
                throw new InvalidOperationException(
                    $"workflow '{workflow.Name}' step '{step.Name}' codeFile not found at runtime: '{step.CodeFile}'.");
            }
            code = await File.ReadAllTextAsync(step.CodeFile, cancellationToken);
            _logger.LogInformation("workflow step='{Step}' codeFile='{File}' bytes={Bytes}", step.Name, step.CodeFile, code.Length);
        }
        else if (!string.IsNullOrWhiteSpace(step.Code))
        {
            code = step.Code;
        }
        else
        {
            throw new InvalidOperationException(
                $"workflow '{workflow.Name}' step '{step.Name}' requires either code or codeFile.");
        }

        var inputs = BuildScriptInputs(results, workflowInput);
        var raw = await _scriptRunner.RunAsync(code, inputs, cancellationToken);
        var output = ToOutputDictionary(raw);
        var resultStr = System.Text.Json.JsonSerializer.Serialize(output);
        results[step.Name] = new WorkflowStepResult { Result = resultStr, Output = output, Raw = raw };
        return resultStr;
    }

    private async Task<string> RunWorkflowApproveStepAsync(
        WorkflowDefinition workflow,
        WorkflowStep step,
        string workflowInput,
        string? runId,
        Dictionary<string, WorkflowStepResult> results,
        CancellationToken cancellationToken)
    {
        if (_approvalService is null)
        {
            throw new InvalidOperationException("workflow approve step requires IApprovalService to be registered.");
        }
        if (string.IsNullOrWhiteSpace(runId))
        {
            throw new InvalidOperationException("workflow approve step must run as part of an asynchronous run (runId required).");
        }

        var timeout = step.Timeout ?? DefaultApprovalTimeout;
        var title = string.IsNullOrWhiteSpace(step.Title) ? step.Name : step.Title!.Trim();
        var summary = string.IsNullOrWhiteSpace(step.Summary) ? step.Name : RenderTemplate(step.Summary!, workflowInput, results);
        var tool = $"workflow.{workflow.Name}.{step.Name}";

        var request = await _approvalService.RequestAsync(runId, tool, summary, timeout, title, cancellationToken);
        if (request.Status != ApprovalStatus.Approved)
        {
            throw new ApprovalRejectedException(request);
        }
        results[step.Name] = WorkflowStepResult.FromString("approved");
        return "approved";
    }

    private static IReadOnlyDictionary<string, object?> BuildScriptInputs(
        Dictionary<string, WorkflowStepResult> results,
        string workflowInput)
    {
        var inputs = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["workflow.input"] = workflowInput,
        };
        foreach (var (name, result) in results)
        {
            inputs[name] = result.Output is not null && result.Output.Count > 0
                ? result.Output
                : (object?)result.Result;
        }
        return inputs;
    }

    private static IReadOnlyDictionary<string, object?> ToOutputDictionary(object? raw)
    {
        var dict = new Dictionary<string, object?>(StringComparer.Ordinal);
        if (raw is null)
        {
            return dict;
        }

        switch (raw)
        {
            case IDictionary<string, object?> typed:
                foreach (var (key, value) in typed)
                {
                    dict[key] = value;
                }
                return dict;
            case System.Collections.IDictionary loose:
                foreach (System.Collections.DictionaryEntry entry in loose)
                {
                    dict[entry.Key?.ToString() ?? ""] = entry.Value;
                }
                return dict;
            default:
                var type = raw.GetType();
                if (type.IsPrimitive || type == typeof(string) || type == typeof(decimal))
                {
                    dict["value"] = raw;
                    return dict;
                }

                foreach (var prop in type.GetProperties())
                {
                    dict[prop.Name] = prop.GetValue(raw);
                }
                return dict;
        }
    }

    private static string RenderTemplate(
        string template,
        string workflowInput,
        IReadOnlyDictionary<string, WorkflowStepResult> stepResults)
    {
        if (string.IsNullOrEmpty(template))
        {
            return workflowInput;
        }

        return TemplateRegex.Replace(template, match =>
        {
            var key = match.Groups["key"].Value.Trim();
            if (string.Equals(key, "workflow.input", StringComparison.Ordinal))
            {
                return workflowInput;
            }

            if (key.StartsWith("steps.", StringComparison.Ordinal))
            {
                var remaining = key["steps.".Length..];
                var firstDot = remaining.IndexOf('.');
                if (firstDot < 0)
                {
                    return stepResults.TryGetValue(remaining, out var r0) ? r0.Result ?? "" : "";
                }

                var stepName = remaining[..firstDot];
                if (!stepResults.TryGetValue(stepName, out var result))
                {
                    return "";
                }

                var accessor = remaining[(firstDot + 1)..];
                if (string.Equals(accessor, "result", StringComparison.Ordinal))
                {
                    return result.Result ?? "";
                }

                if (accessor.StartsWith("output.", StringComparison.Ordinal))
                {
                    var key2 = accessor["output.".Length..];
                    return ResolveNestedValue(result.Output, key2);
                }

                // steps.<step>.<key>(output.<key> の省略形と解釈)
                return ResolveNestedValue(result.Output, accessor);
            }

            return match.Value;
        });
    }

    private static string ResolveNestedValue(IReadOnlyDictionary<string, object?>? output, string key)
    {
        if (output is null)
        {
            return "";
        }

        var parts = key.Split('.');
        object? current = output;
        for (var i = 0; i < parts.Length; i++)
        {
            if (current is IReadOnlyDictionary<string, object?> d1 && d1.TryGetValue(parts[i], out var v1))
            {
                current = v1;
                continue;
            }
            if (current is IDictionary<string, object?> d2 && d2.TryGetValue(parts[i], out var v2))
            {
                current = v2;
                continue;
            }
            if (current is System.Collections.IDictionary d3 && d3.Contains(parts[i]))
            {
                current = d3[parts[i]];
                continue;
            }
            // リフレクション(匿名型等)対応
            if (current is not null && i == 0)
            {
                var prop = current.GetType().GetProperty(parts[i]);
                if (prop is not null)
                {
                    current = prop.GetValue(current);
                    continue;
                }
            }
            return "";
        }

        return current switch
        {
            null => "",
            string s => s,
            _ => System.Text.Json.JsonSerializer.Serialize(current),
        };
    }

    private static readonly Regex TemplateRegex = new(
        @"\$\{(?<key>[a-zA-Z0-9_.\[\]]+)\}",
        RegexOptions.Compiled);

    private async Task<string> RunCoreAsync(
        AgentDefinition def,
        string userMessage,
        string? workingDirectory,
        string? threadId,
        string? runId,
        CancellationToken cancellationToken)
    {
        if (_deterministicE2eResponse)
        {
            return $"E2E response: {userMessage}";
        }

        var model = await ResolveModelAsync(def.Name, cancellationToken);
        var agent = _factory.Create(def, model, workingDirectory);
        _logger.LogInformation("run agent='{Name}' model='{Model}' provider={Provider}",
            def.Name, model.Name, model.Provider);

        var session = await LoadOrCreateSessionAsync(agent, def.Name, threadId, cancellationToken);
        var response = await agent.RunAsync(userMessage, session, cancellationToken: cancellationToken);
        var approvalRequests = HarnessApprovalBridge.GetApprovalRequests(response);
        if (approvalRequests.Count > 0 && (string.IsNullOrWhiteSpace(runId) || _approvalBridge is null))
        {
            throw new InvalidOperationException("This agent run requires HITL approval; submit it through the asynchronous run API.");
        }

        if (approvalRequests.Count > 0)
        {
            var approvalBridge = _approvalBridge
                ?? throw new InvalidOperationException("An approval bridge is required for HITL approval.");
            var approvalRunId = runId
                ?? throw new InvalidOperationException("A run ID is required for HITL approval.");
            while (approvalRequests.Count > 0)
            {
                response = await approvalBridge.ResumeAsync(
                    approvalRunId,
                    agent,
                    session,
                    response,
                    DefaultApprovalTimeout,
                    cancellationToken);
                approvalRequests = HarnessApprovalBridge.GetApprovalRequests(response);
            }
        }

        await PersistSessionAsync(agent, def.Name, threadId, session, cancellationToken);
        await RecordCostAsync(def.Name, model, threadId, runId, response.Usage, cancellationToken);
        return response.ToString();
    }

    private async Task RecordCostAsync(
        string agentName,
        LlmModelSettings model,
        string? threadId,
        string? runId,
        UsageDetails? usage,
        CancellationToken cancellationToken)
    {
        if (_costStore is null)
        {
            return;
        }

        try
        {
            await _costStore.RecordAsync(new CostRecord
            {
                RunId = runId,
                ThreadId = threadId,
                AgentName = agentName,
                ModelName = model.Name,
                Provider = model.Provider.ToString(),
                InputTokens = usage?.InputTokenCount,
                OutputTokens = usage?.OutputTokenCount,
                TotalTokens = usage?.TotalTokenCount,
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "could not record cost usage for agent={AgentName}", agentName);
        }
    }

    private async Task<AgentSession> LoadOrCreateSessionAsync(
        AIAgent agent,
        string agentName,
        string? threadId,
        CancellationToken cancellationToken)
    {
        if (threadId is null)
        {
            return await agent.CreateSessionAsync(cancellationToken);
        }

        if (_sessionStore is null)
        {
            throw new InvalidOperationException("A session store is required when a thread ID is supplied.");
        }

        var persisted = await _sessionStore.LoadAsync(threadId, cancellationToken);
        if (persisted is null)
        {
            return await agent.CreateSessionAsync(cancellationToken);
        }

        if (!string.Equals(persisted.AgentName, agentName, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Thread '{threadId}' belongs to agent '{persisted.AgentName}', not '{agentName}'.");
        }

        using var document = JsonDocument.Parse(persisted.SerializedState);
        return await agent.DeserializeSessionAsync(document.RootElement, cancellationToken: cancellationToken);
    }

    private async Task PersistSessionAsync(
        AIAgent agent,
        string agentName,
        string? threadId,
        AgentSession session,
        CancellationToken cancellationToken)
    {
        if (threadId is null)
        {
            return;
        }

        if (_sessionStore is null)
        {
            throw new InvalidOperationException("A session store is required when a thread ID is supplied.");
        }

        var serializedState = await agent.SerializeSessionAsync(session, cancellationToken: cancellationToken);
        await _sessionStore.SaveAsync(new SessionRecord
        {
            ThreadId = threadId,
            AgentName = agentName,
            SerializedState = serializedState.GetRawText(),
        }, cancellationToken);
    }

    public async IAsyncEnumerable<AgentInvocationUpdate> RunStreamingAsync(
        string agentName,
        string userMessage,
        string? workingDirectory,
        string? threadId,
        string? runId,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (_workflowsByName.ContainsKey(agentName))
        {
            throw new NotSupportedException($"streaming is not supported for workflows: '{agentName}'.");
        }

        var def = Resolve(agentName, userMessage);
        threadId = string.IsNullOrWhiteSpace(threadId) ? null : threadId.Trim();
        var gate = threadId is null
            ? null
            : _threadGates.GetOrAdd(threadId, static _ => new SemaphoreSlim(1, 1));
        if (gate is not null)
        {
            await gate.WaitAsync(cancellationToken);
        }

        try
        {
            await foreach (var update in RunCoreStreamingAsync(
                def, userMessage, workingDirectory, threadId, runId, cancellationToken))
            {
                yield return update;
            }
        }
        finally
        {
            gate?.Release();
        }
    }

    /// <summary>
    /// <see cref="RunCoreAsync"/> のストリーミング版。セッションの復元・保存、コスト記録、
    /// HITL 承認の再開はすべて一括版と同じ順序で行う。
    /// 承認待ちに入った場合は <see cref="AgentApprovalRequiredUpdate"/> を 1 回流して途中経過の配信を打ち切り、
    /// 再開自体は一括版と同じ <see cref="HarnessApprovalBridge.ResumeAsync"/> に任せる。
    /// </summary>
    private async IAsyncEnumerable<AgentInvocationUpdate> RunCoreStreamingAsync(
        AgentDefinition def,
        string userMessage,
        string? workingDirectory,
        string? threadId,
        string? runId,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (_deterministicE2eResponse)
        {
            var deterministic = $"E2E response: {userMessage}";
            foreach (var chunk in SplitForDeterministicStreaming(deterministic))
            {
                yield return new AgentTextDeltaUpdate(chunk);
            }

            yield return new AgentCompletedUpdate(new AgentInvocationResult { Utterance = deterministic });
            yield break;
        }

        var model = await ResolveModelAsync(def.Name, cancellationToken);
        var agent = _factory.Create(def, model, workingDirectory);
        _logger.LogInformation("run-streaming agent='{Name}' model='{Model}' provider={Provider}",
            def.Name, model.Name, model.Provider);

        var session = await LoadOrCreateSessionAsync(agent, def.Name, threadId, cancellationToken);
        var updates = new List<AgentResponseUpdate>();
        var seenToolCallIds = new HashSet<string>(StringComparer.Ordinal);

        await foreach (var update in agent.RunStreamingAsync(
            userMessage, session, cancellationToken: cancellationToken))
        {
            updates.Add(update);

            if (!string.IsNullOrEmpty(update.Text))
            {
                yield return new AgentTextDeltaUpdate(update.Text);
            }

            foreach (var call in update.Contents.OfType<FunctionCallContent>())
            {
                // ストリーミング中の FunctionCallContent は引数が分割されて複数回現れるため、
                // CallId で最初の 1 回だけを通知する。
                if (seenToolCallIds.Add(call.CallId))
                {
                    yield return new AgentToolCallUpdate(new AgentToolCall { ToolName = call.Name });
                }
            }
        }

        var response = updates.ToAgentResponse();
        var approvalRequests = HarnessApprovalBridge.GetApprovalRequests(response);
        if (approvalRequests.Count > 0 && (string.IsNullOrWhiteSpace(runId) || _approvalBridge is null))
        {
            throw new InvalidOperationException("This agent run requires HITL approval; submit it through the asynchronous run API.");
        }

        if (approvalRequests.Count > 0)
        {
            yield return new AgentApprovalRequiredUpdate();

            var approvalBridge = _approvalBridge
                ?? throw new InvalidOperationException("An approval bridge is required for HITL approval.");
            var approvalRunId = runId
                ?? throw new InvalidOperationException("A run ID is required for HITL approval.");
            while (approvalRequests.Count > 0)
            {
                response = await approvalBridge.ResumeAsync(
                    approvalRunId,
                    agent,
                    session,
                    response,
                    DefaultApprovalTimeout,
                    cancellationToken);
                approvalRequests = HarnessApprovalBridge.GetApprovalRequests(response);
            }
        }

        await PersistSessionAsync(agent, def.Name, threadId, session, cancellationToken);
        await RecordCostAsync(def.Name, model, threadId, runId, response.Usage, cancellationToken);
        yield return new AgentCompletedUpdate(new AgentInvocationResult { Utterance = response.ToString() });
    }

    /// <summary>決定論 E2E モードで、ストリーミング経路そのものを検証できるように応答を分割する。</summary>
    private static IEnumerable<string> SplitForDeterministicStreaming(string text)
    {
        const int chunkSize = 16;
        for (var offset = 0; offset < text.Length; offset += chunkSize)
        {
            yield return text.Substring(offset, Math.Min(chunkSize, text.Length - offset));
        }
    }

    private async Task<LlmModelSettings> ResolveModelAsync(string agentName, CancellationToken cancellationToken)
        => await _modelStore.ResolveForAgentAsync(agentName, cancellationToken)
            ?? throw new InvalidOperationException(
                "No LLM model is configured. Add a model on the Models page before running an agent.");

    private AgentDefinition Resolve(string agentName, string userMessage)
    {
        if (string.IsNullOrWhiteSpace(userMessage))
        {
            throw new ArgumentException("user message is empty.", nameof(userMessage));
        }

        if (!_byName.TryGetValue(agentName, out var def))
        {
            throw new ArgumentException($"agent not found: '{agentName}'", nameof(agentName));
        }

        return def;
    }
}
