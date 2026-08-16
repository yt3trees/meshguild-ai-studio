using WorkAgents.Tray;

namespace WorkAgents.UnitTests.Tray;

public class ProcessSupervisorTests
{
    private static ProcessSupervisor CreateSupervisor(
        FakeChildProcessLauncher launcher,
        Func<int, CancellationToken, Task<bool>>? readinessProbe = null)
    {
        var settings = new LauncherSettings { WebPort = 5050, HostPort = 5161 };
        return new ProcessSupervisor(
            settings,
            hostExecutablePath: "C:\\fake\\WorkAgents.Host.exe",
            webExecutablePath: "C:\\fake\\WorkAgents.Web.exe",
            launcher: launcher,
            readinessProbe: readinessProbe ?? ((_, _) => Task.FromResult(true)),
            readinessTimeout: TimeSpan.FromMilliseconds(500),
            readinessPollInterval: TimeSpan.FromMilliseconds(10));
    }

    [Fact]
    public async Task StartAsync_ReadinessSucceeds_TransitionsToRunning()
    {
        var launcher = new FakeChildProcessLauncher();
        using var supervisor = CreateSupervisor(launcher);

        await supervisor.StartAsync();

        Assert.Equal(LauncherPhase.Running, supervisor.State.Phase);
        Assert.Equal(2, launcher.Started.Count);
    }

    [Fact]
    public async Task StartAsync_ReadinessNeverSucceeds_TransitionsToError()
    {
        var launcher = new FakeChildProcessLauncher();
        using var supervisor = CreateSupervisor(launcher, readinessProbe: (_, _) => Task.FromResult(false));

        await supervisor.StartAsync();

        Assert.Equal(LauncherPhase.Error, supervisor.State.Phase);
    }

    [Fact]
    public async Task UnexpectedChildExit_WhileRunning_TransitionsToError()
    {
        var launcher = new FakeChildProcessLauncher();
        using var supervisor = CreateSupervisor(launcher);
        await supervisor.StartAsync();
        Assert.Equal(LauncherPhase.Running, supervisor.State.Phase);

        launcher.Started[0].SimulateExit(); // Hostプロセスが予期せず終了(クラッシュ)

        Assert.Equal(LauncherPhase.Error, supervisor.State.Phase);
    }

    [Fact]
    public async Task RestartAsync_FromRunning_EndsInRunning()
    {
        var launcher = new FakeChildProcessLauncher();
        using var supervisor = CreateSupervisor(launcher);
        await supervisor.StartAsync();

        await supervisor.RestartAsync();

        Assert.Equal(LauncherPhase.Running, supervisor.State.Phase);
        Assert.Equal(4, launcher.Started.Count); // 初回2 + 再起動後2
        Assert.True(launcher.Started[0].Killed);
        Assert.True(launcher.Started[1].Killed);
    }

    [Fact]
    public async Task RestartAsync_CalledTwiceConcurrently_SecondCallIsIgnored()
    {
        var launcher = new FakeChildProcessLauncher();
        var initialGate = new TaskCompletionSource();
        var restartGate = new TaskCompletionSource();
        var probeCallCount = 0;
        using var supervisor = CreateSupervisor(
            launcher,
            readinessProbe: async (_, ct) =>
            {
                var call = Interlocked.Increment(ref probeCallCount);
                var gate = call == 1 ? initialGate : restartGate;
                await gate.Task.WaitAsync(ct);
                return true;
            });

        initialGate.SetResult(); // 初回起動は即座に完了させる
        await supervisor.StartAsync();
        Assert.Equal(LauncherPhase.Running, supervisor.State.Phase);

        // 1回目の「更新」を開始する(readinessProbeが未解決のためUpdatingで止まる)
        var firstRestart = supervisor.RestartAsync();

        // 2回目の「更新」は多重実行として無視される(FR-014): 状態はUpdatingのまま変化しない
        await supervisor.RestartAsync();
        Assert.Equal(LauncherPhase.Updating, supervisor.State.Phase);

        restartGate.SetResult();
        await firstRestart;
        Assert.Equal(LauncherPhase.Running, supervisor.State.Phase);
    }

    [Fact]
    public async Task ShutdownAsync_FromRunning_TransitionsToExitingAndKillsChildren()
    {
        var launcher = new FakeChildProcessLauncher();
        using var supervisor = CreateSupervisor(launcher);
        await supervisor.StartAsync();

        await supervisor.ShutdownAsync();

        Assert.Equal(LauncherPhase.Exiting, supervisor.State.Phase);
        Assert.All(launcher.Started, p => Assert.True(p.Killed));
    }

    [Fact]
    public async Task StartAsync_AdditionalConfiguration_TranslatesColonKeysToDoubleUnderscoreEnvVars()
    {
        var launcher = new FakeChildProcessLauncher();
        var settings = new LauncherSettings
        {
            WebPort = 5050,
            HostPort = 5161,
            AdditionalConfiguration = new Dictionary<string, string>
            {
                ["Runs:QueueCapacity"] = "50",
                ["GitAuth:AppId"] = "12345",
            },
        };
        using var supervisor = new ProcessSupervisor(
            settings,
            hostExecutablePath: "C:\\fake\\WorkAgents.Host.exe",
            webExecutablePath: "C:\\fake\\WorkAgents.Web.exe",
            launcher: launcher,
            readinessProbe: (_, _) => Task.FromResult(true));

        await supervisor.StartAsync();

        Assert.All(launcher.StartInfos, info =>
        {
            Assert.Equal("50", info.EnvironmentVariables["Runs__QueueCapacity"]);
            Assert.Equal("12345", info.EnvironmentVariables["GitAuth__AppId"]);
        });
    }

    [Fact]
    public async Task StartAsync_McpEnabled_PropagatesMcpEnvironmentVariable()
    {
        var launcher = new FakeChildProcessLauncher();
        using var supervisor = new ProcessSupervisor(
            new LauncherSettings { WebPort = 5050, HostPort = 5161, McpEnabled = true },
            hostExecutablePath: "C:\\fake\\WorkAgents.Host.exe",
            webExecutablePath: "C:\\fake\\WorkAgents.Web.exe",
            launcher: launcher,
            readinessProbe: (_, _) => Task.FromResult(true));

        await supervisor.StartAsync();

        Assert.All(launcher.StartInfos, info => Assert.Equal("true", info.EnvironmentVariables["Mcp__Enabled"]));
    }

    [Fact]
    public async Task StartAsync_MultipleDefinitionSources_UsesOrderedIndexedEnvironmentVariables()
    {
        var launcher = new FakeChildProcessLauncher();
        var settings = new LauncherSettings
        {
            WebPort = 5050,
            HostPort = 5161,
            DatabasePath = @"D:\state\work-agents.db",
            AdditionalAgentDefinitionPaths =
            [
                @"D:\teams\sales-agents",
                @"D:\teams\shared-agents",
                @"D:\teams\experiments",
            ],
        };
        using var supervisor = new ProcessSupervisor(
            settings,
            hostExecutablePath: "C:\\fake\\WorkAgents.Host.exe",
            webExecutablePath: "C:\\fake\\WorkAgents.Web.exe",
            launcher: launcher,
            readinessProbe: (_, _) => Task.FromResult(true));

        await supervisor.StartAsync();

        Assert.All(launcher.StartInfos, info =>
        {
            Assert.Equal("standard", info.EnvironmentVariables["Agents__DefinitionSources__0__Label"]);
            Assert.Equal("D:\\state\\work-agents.db", info.EnvironmentVariables["Runs__DatabasePath"]);
            Assert.Equal("D:\\teams\\sales-agents", info.EnvironmentVariables["Agents__DefinitionSources__1__Path"]);
            Assert.Equal("D:\\teams\\shared-agents", info.EnvironmentVariables["Agents__DefinitionSources__2__Path"]);
            Assert.Equal("D:\\teams\\experiments", info.EnvironmentVariables["Agents__DefinitionSources__3__Path"]);
            Assert.Equal("additional-1", info.EnvironmentVariables["Agents__DefinitionSources__1__Label"]);
            Assert.Equal("additional-2", info.EnvironmentVariables["Agents__DefinitionSources__2__Label"]);
            Assert.Equal("additional-3", info.EnvironmentVariables["Agents__DefinitionSources__3__Label"]);
        });
    }

    [Fact]
    public async Task StartAsync_PublishedLayout_UsesCommonSiblingDefinitionRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "WorkAgentsTrayPublished_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "definitions", "agents"));

        try
        {
            var launcher = new FakeChildProcessLauncher();
            var settings = new LauncherSettings { WebPort = 5050, HostPort = 5161 };
            using var supervisor = new ProcessSupervisor(
                settings,
                hostExecutablePath: Path.Combine(root, "WorkAgents.Host", "WorkAgents.Host.exe"),
                webExecutablePath: Path.Combine(root, "WorkAgents.Web", "WorkAgents.Web.exe"),
                launcher: launcher,
                readinessProbe: (_, _) => Task.FromResult(true));

            await supervisor.StartAsync();

            var expectedRoot = Path.GetFullPath(Path.Combine(root, "definitions"));
            Assert.All(launcher.StartInfos, info =>
            {
                Assert.Equal("standard", info.EnvironmentVariables["Agents__DefinitionSources__0__Label"]);
                Assert.Equal(expectedRoot, info.EnvironmentVariables["Agents__DefinitionSources__0__Path"]);
            });
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ShutdownAsync_ChildExitEvent_DoesNotOverrideExitingPhase()
    {
        var launcher = new FakeChildProcessLauncher();
        using var supervisor = CreateSupervisor(launcher);
        await supervisor.StartAsync();

        await supervisor.ShutdownAsync();

        Assert.Equal(LauncherPhase.Exiting, supervisor.State.Phase); // OnChildExitedがErrorへ誤遷移させないこと
    }
}
