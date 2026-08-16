using System.Collections;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;
using Microsoft.Extensions.Logging;
using WorkAgents.Core;
using WorkAgents.Core.Abstractions;

namespace WorkAgents.Infrastructure.Workflows;

/// <summary>
/// Roslyn in-process で C# スクリプト(code ステップ)を実行する(5.13.1)。
/// Host プロセス権限で動くため信頼できる作成者のみ配置可。Local プロファイル以降の分離は M7 で検討。
/// <c>Inputs</c>(IDictionary&lt;string,object?&gt;)を globals として注入し、戻り値をそのまま返す。
/// 共通 using として System / System.IO / System.Linq / System.Collections.Generic / System.Text を事前バインド。
/// </summary>
public sealed class RoslynWorkflowScriptRunner : IWorkflowScriptRunner
{
    private readonly ILogger<RoslynWorkflowScriptRunner>? _logger;

    public RoslynWorkflowScriptRunner(ILogger<RoslynWorkflowScriptRunner>? logger = null)
    {
        _logger = logger;
    }

    public async Task<object?> RunAsync(
        string code,
        IReadOnlyDictionary<string, object?> inputs,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);

        var globals = new WorkflowScriptGlobals(inputs);
        var options = ScriptOptions.Default
            .WithImports(
                "System",
                "System.IO",
                "System.Linq",
                "System.Text",
                "System.Collections.Generic",
                "System.Globalization",
                "System.Threading",
                "System.Threading.Tasks")
            .WithReferences(
                typeof(object).Assembly,
                typeof(Enumerable).Assembly,
                typeof(Console).Assembly,
                typeof(File).Assembly,
                typeof(Dictionary<,>).Assembly,
                typeof(Task).Assembly);

        _logger?.LogInformation("running workflow code step (Inputs=[{Keys}])", string.Join(",", inputs.Keys));
        return await CSharpScript.EvaluateAsync(code, options, globals, typeof(WorkflowScriptGlobals), ct);
    }
}

/// <summary>スクリプト内から <c>Inputs["steps.<name>.output.<key>"]</c> 等で参照可能な globals。</summary>
public sealed class WorkflowScriptGlobals
{
    public WorkflowScriptGlobals(IReadOnlyDictionary<string, object?> inputs)
    {
        Inputs = inputs;
    }

    public IReadOnlyDictionary<string, object?> Inputs { get; }

    /// <summary>Dictionary を文字列→object の IDictionary としても読める便利アクセス(スクリプト性用)。</summary>
    public object? this[string key]
    {
        get => Inputs.TryGetValue(key, out var v) ? v : null;
    }
}