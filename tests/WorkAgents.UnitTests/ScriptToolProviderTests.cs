using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.AI;
using WorkAgents.Agents.Tools;

namespace WorkAgents.UnitTests;

/// <summary>
/// <see cref="ScriptToolProvider"/> の実プロセス起動を検証する統合テスト(contracts/script-tool-contract.md)。
/// テスト環境に <c>node</c>/<c>python</c> が無い場合は、該当ケースを早期returnでスキップする。
/// </summary>
public sealed class ScriptToolProviderTests
{
    [Fact]
    public async Task InvokeAsync_NodeScript_EchoesArgumentsViaStdinStdout()
    {
        if (!IsRuntimeAvailable("node"))
        {
            return;
        }

        var dir = CreateTempDir();
        try
        {
            var scriptPath = Path.Combine(dir, "echo.js");
            File.WriteAllText(scriptPath, """
                let chunks = [];
                process.stdin.on('data', d => chunks.push(d));
                process.stdin.on('end', () => {
                    const input = JSON.parse(Buffer.concat(chunks).toString('utf8') || '{}');
                    process.stdout.write(JSON.stringify({ received: input }));
                });
                """);

            var manifest = CreateManifest("echo_tool", ScriptToolRuntime.Node, "echo.js", timeoutSeconds: 10);
            var provider = new ScriptToolProvider(manifest, scriptPath);
            var registration = Assert.Single(provider.CreateTools(new NullServiceProvider()));
            var function = Assert.IsAssignableFrom<AIFunction>(registration.Tool);

            var arguments = new AIFunctionArguments(new Dictionary<string, object?> { ["message"] = "hello" });
            var result = await function.InvokeAsync(arguments);

            var json = Assert.IsType<JsonElement>(result);
            Assert.Equal("hello", json.GetProperty("received").GetProperty("message").GetString());
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task InvokeAsync_PythonScript_EchoesArgumentsViaStdinStdout()
    {
        if (!IsRuntimeAvailable("python"))
        {
            return;
        }

        var dir = CreateTempDir();
        try
        {
            var scriptPath = Path.Combine(dir, "echo.py");
            File.WriteAllText(scriptPath, """
                import sys, json
                data = json.loads(sys.stdin.read() or "{}")
                print(json.dumps({"received": data}))
                """);

            var manifest = CreateManifest("echo_tool_py", ScriptToolRuntime.Python, "echo.py", timeoutSeconds: 10);
            var provider = new ScriptToolProvider(manifest, scriptPath);
            var registration = Assert.Single(provider.CreateTools(new NullServiceProvider()));
            var function = Assert.IsAssignableFrom<AIFunction>(registration.Tool);

            var arguments = new AIFunctionArguments(new Dictionary<string, object?> { ["message"] = "hi" });
            var result = await function.InvokeAsync(arguments);

            var json = Assert.IsType<JsonElement>(result);
            Assert.Equal("hi", json.GetProperty("received").GetProperty("message").GetString());
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task InvokeAsync_NonZeroExitCode_ThrowsWithoutLeakingStderrToException()
    {
        if (!IsRuntimeAvailable("node"))
        {
            return;
        }

        var dir = CreateTempDir();
        try
        {
            var scriptPath = Path.Combine(dir, "fail.js");
            File.WriteAllText(scriptPath, """
                console.error('SENSITIVE INTERNAL DETAIL');
                process.exit(1);
                """);

            var manifest = CreateManifest("fail_tool", ScriptToolRuntime.Node, "fail.js", timeoutSeconds: 10);
            var provider = new ScriptToolProvider(manifest, scriptPath);
            var function = Assert.IsAssignableFrom<AIFunction>(provider.CreateTools(new NullServiceProvider())[0].Tool);

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(
                () => function.InvokeAsync(new AIFunctionArguments()).AsTask());

            Assert.DoesNotContain("SENSITIVE INTERNAL DETAIL", ex.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task InvokeAsync_ExceedsTimeout_ThrowsTimeoutException()
    {
        if (!IsRuntimeAvailable("node"))
        {
            return;
        }

        var dir = CreateTempDir();
        try
        {
            var scriptPath = Path.Combine(dir, "slow.js");
            File.WriteAllText(scriptPath, "setTimeout(() => process.stdout.write('{}'), 5000);");

            var manifest = CreateManifest("slow_tool", ScriptToolRuntime.Node, "slow.js", timeoutSeconds: 1);
            var provider = new ScriptToolProvider(manifest, scriptPath);
            var function = Assert.IsAssignableFrom<AIFunction>(provider.CreateTools(new NullServiceProvider())[0].Tool);

            await Assert.ThrowsAsync<TimeoutException>(() => function.InvokeAsync(new AIFunctionArguments()).AsTask());
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    private static bool IsRuntimeAvailable(string command)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = command,
                ArgumentList = { "--version" },
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            });
            process?.WaitForExit(5000);
            return process is { ExitCode: 0 };
        }
        catch
        {
            return false;
        }
    }

    private static string CreateTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "work-agents-tests", $"script-tool-provider-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static ScriptToolManifest CreateManifest(string name, ScriptToolRuntime runtime, string entryPoint, int timeoutSeconds)
        => new()
        {
            Name = name,
            Description = $"{name} description",
            AgentName = "meeting-agent",
            Runtime = runtime,
            EntryPoint = entryPoint,
            Approval = "automatic",
            TimeoutSeconds = timeoutSeconds,
        };

    private sealed class NullServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }
}
