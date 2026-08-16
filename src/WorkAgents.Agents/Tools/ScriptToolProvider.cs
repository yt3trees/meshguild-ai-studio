using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace WorkAgents.Agents.Tools;

/// <summary>
/// <see cref="ScriptToolManifest"/> から <see cref="AgentToolRegistration"/> を生成する
/// <see cref="IAgentToolProvider"/> 実装(contracts/script-tool-contract.md、FR-010)。
/// DLLプラグイン(<see cref="ToolPluginLoader"/>)と同じ登録パイプラインに合流し、承認ポリシー適用・
/// 名称衝突時の上書きは <see cref="AgentToolCatalog"/> 側の既存ロジックをそのまま利用する。
/// </summary>
public sealed class ScriptToolProvider : IAgentToolProvider
{
    private readonly ScriptToolManifest _manifest;
    private readonly string _entryPointPath;

    public ScriptToolProvider(ScriptToolManifest manifest, string entryPointPath)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentException.ThrowIfNullOrWhiteSpace(entryPointPath);
        _manifest = manifest;
        _entryPointPath = entryPointPath;
    }

    public string AgentName => _manifest.AgentName;

    public IReadOnlyList<AgentToolRegistration> CreateTools(IServiceProvider services)
    {
        var logger = services.GetService<ILogger<ScriptToolProvider>>();
        var function = new ScriptToolAIFunction(_manifest, _entryPointPath, logger);
        return
        [
            new AgentToolRegistration(_manifest.Name, _manifest.Description, "script", _manifest.Approval, function),
        ];
    }
}

/// <summary>
/// 呼び出しごとに <c>node</c>/<c>python</c> の子プロセスを起動し、標準入出力のJSON1行でIPCする
/// <see cref="AIFunction"/> 実装(research.md「6.」、contracts/script-tool-contract.md)。
/// </summary>
internal sealed class ScriptToolAIFunction : AIFunction
{
    private readonly ScriptToolManifest _manifest;
    private readonly string _entryPointPath;
    private readonly ILogger? _logger;
    private readonly JsonElement _jsonSchema;

    public ScriptToolAIFunction(ScriptToolManifest manifest, string entryPointPath, ILogger? logger)
    {
        _manifest = manifest;
        _entryPointPath = entryPointPath;
        _logger = logger;
        _jsonSchema = BuildJsonSchema(manifest.Parameters);
    }

    public override string Name => _manifest.Name;

    public override string Description => _manifest.Description;

    public override JsonElement JsonSchema => _jsonSchema;

    protected override async ValueTask<object?> InvokeCoreAsync(AIFunctionArguments arguments, CancellationToken cancellationToken)
    {
        var argsPayload = new Dictionary<string, object?>();
        foreach (var pair in arguments)
        {
            argsPayload[pair.Key] = pair.Value;
        }
        var inputJson = JsonSerializer.Serialize(argsPayload);

        var command = _manifest.Runtime switch
        {
            ScriptToolRuntime.Node => "node",
            ScriptToolRuntime.Python => "python",
            _ => throw new InvalidOperationException($"unsupported script tool runtime: {_manifest.Runtime}"),
        };

        var startInfo = new ProcessStartInfo
        {
            FileName = command,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add(_entryPointPath);

        using var process = new Process { StartInfo = startInfo };
        try
        {
            process.Start();
        }
        catch (Exception ex)
        {
            // ランタイム不在はロード時ではなく呼び出し時に検出する方針(research.md「9.」)。
            _logger?.LogError(ex, "failed to start script tool '{Name}' process ('{Command}')", _manifest.Name, command);
            throw new InvalidOperationException($"script tool '{_manifest.Name}' could not be started. Is '{command}' installed and on PATH?");
        }

        await process.StandardInput.WriteLineAsync(inputJson);
        process.StandardInput.Close();

        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);

        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(_manifest.TimeoutSeconds));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
        try
        {
            await process.WaitForExitAsync(linkedCts.Token);
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
        {
            TryKill(process, _manifest.Name, _logger);
            throw new TimeoutException($"script tool '{_manifest.Name}' timed out after {_manifest.TimeoutSeconds}s");
        }

        var stdout = await stdoutTask;
        var stderr = await stderrTask;

        if (process.ExitCode != 0)
        {
            // 憲法I: 例外の全文は保存せず、詳細はプロセス管理ログに限定し利用者へは一般化メッセージを返す。
            _logger?.LogError(
                "script tool '{Name}' exited with code {ExitCode}. stderr: {Stderr}",
                _manifest.Name, process.ExitCode, stderr);
            throw new InvalidOperationException($"script tool '{_manifest.Name}' failed (exit code {process.ExitCode}).");
        }

        if (string.IsNullOrWhiteSpace(stdout))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(stdout);
            return doc.RootElement.Clone();
        }
        catch (JsonException ex)
        {
            _logger?.LogError(ex, "script tool '{Name}' returned invalid JSON: {Stdout}", _manifest.Name, stdout);
            throw new InvalidOperationException($"script tool '{_manifest.Name}' returned invalid output.");
        }
    }

    private static void TryKill(Process process, string toolName, ILogger? logger)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "failed to kill timed-out script tool process for '{Name}'", toolName);
        }
    }

    private static JsonElement BuildJsonSchema(IReadOnlyDictionary<string, object?> parameters)
    {
        var json = JsonSerializer.Serialize(parameters);
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }
}
