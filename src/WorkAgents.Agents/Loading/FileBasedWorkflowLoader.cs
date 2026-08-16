using Microsoft.Extensions.Logging;
using WorkAgents.Agents.Configuration;
using WorkAgents.Core;

namespace WorkAgents.Agents.Loading;

/// <summary>
/// ファイルベースワークフローローダ(5.13.1)。<c>workflows/</c> を走査し、各フォルダの
/// <c>workflow.yaml</c> から <see cref="WorkflowDefinition"/> を構築する。<see cref="FileBasedAgentLoader"/>
/// と並列規約。MAF 本体に declarative workflow シンボルが無いため独自軽量実装(D8)。
/// 複数の定義ソースをマージ読み込みする場合は <see cref="LoadFromSources"/> を使う
/// (specs/006-team-config-distribution)。
/// </summary>
public sealed class FileBasedWorkflowLoader
{
    private readonly string _workflowsRoot;
    private readonly ILogger<FileBasedWorkflowLoader>? _logger;

    public FileBasedWorkflowLoader(string workflowsRoot, ILogger<FileBasedWorkflowLoader>? logger = null)
    {
        _workflowsRoot = workflowsRoot;
        _logger = logger;
    }

    public IReadOnlyList<WorkflowDefinition> Load()
    {
        var list = new List<WorkflowDefinition>();
        if (!Directory.Exists(_workflowsRoot))
        {
            _logger?.LogInformation("workflows root not found: {Root}", _workflowsRoot);
            return list;
        }

        foreach (var dir in Directory.EnumerateDirectories(_workflowsRoot))
        {
            var definition = BuildDefinition(dir, "standard", Array.Empty<string>());
            if (definition is not null)
            {
                list.Add(definition);
            }
        }

        _logger?.LogInformation("loaded {Count} workflow(s) from {Root}", list.Count, _workflowsRoot);
        return list;
    }

    /// <summary>
    /// 複数の定義ソースを順に走査し、同名ワークフローを後勝ちでマージ読み込みする
    /// (FR-002・FR-005)。<c>sources[].Path</c> 配下の <c>workflows/</c> サブディレクトリが対象。
    /// </summary>
    public IReadOnlyList<WorkflowDefinition> LoadFromSources(
        IReadOnlyList<DefinitionSourceEntry> sources,
        ILogger<DefinitionSourceResolver>? resolverLogger = null)
    {
        ArgumentNullException.ThrowIfNull(sources);
        var resolver = new DefinitionSourceResolver(sources, resolverLogger);
        var (folders, _) = resolver.ResolveFolders("workflows");

        var list = new List<WorkflowDefinition>();
        foreach (var folder in folders)
        {
            var definition = BuildDefinition(folder.FolderPath, folder.SourceLabel, folder.OverriddenSourceLabels);
            if (definition is not null)
            {
                list.Add(definition);
            }
        }

        _logger?.LogInformation("loaded {Count} workflow(s) from {SourceCount} source(s)", list.Count, sources.Count);
        return list;
    }

    private WorkflowDefinition? BuildDefinition(string dir, string sourceLabel, IReadOnlyList<string> overriddenSourceLabels)
    {
        var yamlPath = Path.Combine(dir, "workflow.yaml");
        if (!File.Exists(yamlPath))
        {
            return null;
        }

        try
        {
            var yamlText = File.ReadAllText(yamlPath);
            var yaml = WorkflowYamlSerializer.Deserialize(yamlText);

            var name = string.IsNullOrWhiteSpace(yaml.Name) ? Path.GetFileName(dir) : yaml.Name!.Trim();
            var steps = new List<WorkflowStep>();
            if (yaml.Steps is not null)
            {
                foreach (var st in yaml.Steps)
                {
                    var stepName = string.IsNullOrWhiteSpace(st.Name)
                        ? $"step-{steps.Count + 1}"
                        : st.Name!.Trim();
                    var kind = WorkflowYamlSerializer.ParseKind(st.Kind);
                    var timeout = st.TimeoutMinutes is > 0
                        ? TimeSpan.FromMinutes(st.TimeoutMinutes.Value)
                        : (TimeSpan?)null;

                    string? codeFileAbsolute = null;
                    if (!string.IsNullOrWhiteSpace(st.CodeFile))
                    {
                        var candidate = Path.IsPathRooted(st.CodeFile)
                            ? st.CodeFile!
                            : Path.Combine(dir, st.CodeFile!);
                        var full = Path.GetFullPath(candidate);
                        if (!File.Exists(full))
                        {
                            _logger?.LogError(
                                "workflow '{Workflow}' step '{Step}' codeFile not found: '{CodeFile}' (resolved='{Resolved}')",
                                name, stepName, st.CodeFile, full);
                        }
                        codeFileAbsolute = full;
                    }

                    steps.Add(new WorkflowStep
                    {
                        Name = stepName,
                        Kind = kind,
                        Agent = st.Agent?.Trim(),
                        Input = st.Input,
                        Code = st.Code,
                        CodeFile = codeFileAbsolute,
                        Title = st.Title,
                        Summary = st.Summary,
                        Timeout = timeout,
                    });
                }
            }

            if (steps.Count == 0)
            {
                _logger?.LogWarning("workflow '{Name}' has no steps (folder={Folder})", name, dir);
            }

            return new WorkflowDefinition
            {
                Name = name,
                DisplayName = string.IsNullOrWhiteSpace(yaml.DisplayName) ? name : yaml.DisplayName!.Trim(),
                Description = yaml.Description ?? "",
                FolderPath = dir,
                Steps = steps,
                ScheduleCron = string.IsNullOrWhiteSpace(yaml.Schedule?.Cron) ? null : yaml.Schedule!.Cron!.Trim(),
                SourceLabel = sourceLabel,
                OverriddenSourceLabels = overriddenSourceLabels,
            };
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "failed to load workflow from {Dir}", dir);
            return null;
        }
    }

    /// <summary><c>workflows/</c> のルートを解決する。</summary>
    public static string ResolveWorkflowsRoot(string baseDir)
        => DefinitionRootResolver.ResolveDirectory(baseDir, "workflows");
}
