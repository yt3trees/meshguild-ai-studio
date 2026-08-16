using System.Diagnostics;
using System.Net.Http;

namespace WorkAgents.Tray;

/// <summary>
/// Host/Web子プロセスの起動・監視・再起動・終了を担う、UI非依存の中核クラス。
/// <see cref="LauncherState"/>を保持し、子プロセスの起動完了検知(research.md「2.」)、
/// 予期せぬ終了時のError遷移(FR-013a)、多重実行防止(FR-014)を実装する。
/// </summary>
public sealed class ProcessSupervisor : IDisposable
{
    private static readonly HttpClient SharedHttpClient = new() { Timeout = TimeSpan.FromSeconds(5) };

    private readonly IChildProcessLauncher _launcher;
    private readonly JobObjectGuard _jobGuard;
    private readonly bool _ownsJobGuard;
    private readonly string _hostExecutablePath;
    private readonly string _webExecutablePath;
    private readonly Func<int, CancellationToken, Task<bool>> _readinessProbe;
    private readonly TimeSpan _readinessTimeout;
    private readonly TimeSpan _readinessPollInterval;
    private readonly object _sync = new();

    private IChildProcess? _hostProcess;
    private IChildProcess? _webProcess;
    private bool _operationInProgress;

    public ProcessSupervisor(
        LauncherSettings settings,
        string hostExecutablePath,
        string webExecutablePath,
        IChildProcessLauncher? launcher = null,
        JobObjectGuard? jobGuard = null,
        Func<int, CancellationToken, Task<bool>>? readinessProbe = null,
        TimeSpan? readinessTimeout = null,
        TimeSpan? readinessPollInterval = null)
    {
        Settings = settings;
        _hostExecutablePath = hostExecutablePath;
        _webExecutablePath = webExecutablePath;
        _launcher = launcher ?? new RealChildProcessLauncher();
        _ownsJobGuard = jobGuard is null;
        _jobGuard = jobGuard ?? new JobObjectGuard();
        _readinessProbe = readinessProbe ?? DefaultReadinessProbeAsync;
        _readinessTimeout = readinessTimeout ?? TimeSpan.FromSeconds(30);
        _readinessPollInterval = readinessPollInterval ?? TimeSpan.FromMilliseconds(300);
    }

    public LauncherState State { get; } = new();

    public LauncherSettings Settings { get; }

    /// <summary>State遷移や操作の受理/拒否が起きるたびに発火する(UI側のアイコン更新用)。</summary>
    public event Action? StateChanged;

    /// <summary>
    /// 子プロセスを起動し、Hostの起動完了(<see cref="_readinessProbe"/>成功)を待って
    /// <see cref="LauncherPhase.Running"/>へ遷移する。起動処理自体もFR-014の多重実行防止対象。
    /// </summary>
    public async Task StartAsync(CancellationToken ct = default)
    {
        if (!TryBeginOperation())
        {
            return;
        }

        try
        {
            LaunchChildren();
            await WaitForReadyAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            EndOperation();
        }
    }

    /// <summary>
    /// 「更新」操作(FR-005): Host→Webの順で子プロセスを終了し、同じ設定で再起動する。
    /// 実行中に同操作/終了操作が重ねて呼ばれても<see cref="TryBeginOperation"/>で無視する(FR-014)。
    /// </summary>
    public async Task RestartAsync(CancellationToken ct = default)
    {
        if (!TryBeginOperation())
        {
            return;
        }

        try
        {
            lock (_sync)
            {
                if (State.CanTransitionTo(LauncherPhase.Updating))
                {
                    State.TransitionTo(LauncherPhase.Updating);
                }
            }
            StateChanged?.Invoke();

            StopChildren();
            LaunchChildren();
            await WaitForReadyAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            EndOperation();
        }
    }

    /// <summary>「終了」操作(FR-008): 子プロセスを終了し<see cref="LauncherPhase.Exiting"/>へ遷移する。</summary>
    public Task ShutdownAsync()
    {
        if (!TryBeginOperation())
        {
            return Task.CompletedTask;
        }

        try
        {
            lock (_sync)
            {
                if (State.CanTransitionTo(LauncherPhase.Exiting))
                {
                    State.TransitionTo(LauncherPhase.Exiting);
                }
            }
            StateChanged?.Invoke();
            StopChildren();
        }
        finally
        {
            EndOperation();
        }

        return Task.CompletedTask;
    }

    private bool TryBeginOperation()
    {
        lock (_sync)
        {
            if (_operationInProgress)
            {
                return false;
            }

            _operationInProgress = true;
            return true;
        }
    }

    private void EndOperation()
    {
        lock (_sync)
        {
            _operationInProgress = false;
        }
    }

    private void LaunchChildren()
    {
        lock (_sync)
        {
            _hostProcess = StartChild(_hostExecutablePath, Settings.HostPort, hostBaseUrl: null);
            _webProcess = StartChild(_webExecutablePath, Settings.WebPort, hostBaseUrl: $"http://localhost:{Settings.HostPort}");
        }
    }

    /// <summary>ASPNETCORE_URLS/Orchestration__HostBaseUrlの環境変数経由でポート設定を子プロセスへ伝える(research.md「6.」)。</summary>
    private IChildProcess StartChild(string exePath, int port, string? hostBaseUrl)
    {
        var workingDirectoryRaw = Path.GetDirectoryName(exePath);
        var workingDirectory = string.IsNullOrEmpty(workingDirectoryRaw) ? AppContext.BaseDirectory : workingDirectoryRaw;
        var startInfo = new ProcessStartInfo
        {
            FileName = exePath,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = workingDirectory,
        };
        startInfo.EnvironmentVariables["ASPNETCORE_URLS"] = $"http://localhost:{port}";
        if (hostBaseUrl is not null)
        {
            startInfo.EnvironmentVariables["Orchestration__HostBaseUrl"] = hostBaseUrl;
        }

        // 未指定(null/空)の項目は環境変数を渡さず、Host/Web自身の既定値をそのまま使わせる。
        if (!string.IsNullOrWhiteSpace(Settings.WorkspaceRoot))
        {
            startInfo.EnvironmentVariables["Workspace__Root"] = Settings.WorkspaceRoot;
        }

        if (!string.IsNullOrWhiteSpace(Settings.ArtifactsRoot))
        {
            startInfo.EnvironmentVariables["Artifacts__Root"] = Settings.ArtifactsRoot;
        }

        if (!string.IsNullOrWhiteSpace(Settings.DatabasePath))
        {
            startInfo.EnvironmentVariables["Runs__DatabasePath"] = Settings.DatabasePath;
        }

        startInfo.EnvironmentVariables["Mcp__Enabled"] = Settings.McpEnabled.ToString().ToLowerInvariant();

        var additionalDefinitionPaths = Settings.GetAdditionalAgentDefinitionPaths();
        var commonDefinitionRoot = ResolveCommonDefinitionRoot(workingDirectory);
        if (commonDefinitionRoot is not null || additionalDefinitionPaths.Count > 0)
        {
            // インデックス付き環境変数でAgents:DefinitionSourcesを丸ごと定義し直す。
            // publish時はHost/Webの兄弟にあるdefinitions/を共通の標準ソースとして使う。
            startInfo.EnvironmentVariables["Agents__DefinitionSources__0__Label"] = "standard";
            startInfo.EnvironmentVariables["Agents__DefinitionSources__0__Path"] =
                commonDefinitionRoot ?? workingDirectory;

            for (var index = 0; index < additionalDefinitionPaths.Count; index++)
            {
                var sourceIndex = index + 1;
                startInfo.EnvironmentVariables[$"Agents__DefinitionSources__{sourceIndex}__Label"] =
                    $"additional-{index + 1}";
                startInfo.EnvironmentVariables[$"Agents__DefinitionSources__{sourceIndex}__Path"] =
                    additionalDefinitionPaths[index];
            }
        }

        // 汎用オーバーライド(manual/_pages/configuration.mdの表にある任意のキー)。
        // ASP.NET Coreの設定キー記法(":"区切り)を環境変数用の"__"区切りへ変換して渡す。
        if (Settings.AdditionalConfiguration is { Count: > 0 } additionalConfiguration)
        {
            foreach (var (key, value) in additionalConfiguration)
            {
                startInfo.EnvironmentVariables[key.Replace(":", "__")] = value;
            }
        }

        var process = _launcher.Start(startInfo);
        process.Exited += OnChildExited;
        _jobGuard.Assign(process);
        return process;
    }

    private static string? ResolveCommonDefinitionRoot(string workingDirectory)
    {
        var root = Path.GetFullPath(Path.Combine(workingDirectory, "..", "definitions"));
        return Directory.Exists(Path.Combine(root, "agents")) ? root : null;
    }

    /// <summary>FR-013a: 稼働中の予期せぬ終了は自動再起動せずErrorへ遷移する。更新/終了処理中の想定内終了は無視する。</summary>
    private void OnChildExited(object? sender, EventArgs e)
    {
        lock (_sync)
        {
            if (State.Phase is LauncherPhase.Updating or LauncherPhase.Exiting)
            {
                return;
            }

            if (State.CanTransitionTo(LauncherPhase.Error))
            {
                State.TransitionTo(
                    LauncherPhase.Error,
                    "Host/Webプロセスが予期せず終了しました。トレイメニューから「更新」を選んで再起動してください。");
            }
        }

        StateChanged?.Invoke();
    }

    private async Task WaitForReadyAsync(CancellationToken ct)
    {
        var deadline = DateTimeOffset.UtcNow + _readinessTimeout;

        while (DateTimeOffset.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();

            if (await _readinessProbe(Settings.HostPort, ct).ConfigureAwait(false))
            {
                lock (_sync)
                {
                    if (State.CanTransitionTo(LauncherPhase.Running))
                    {
                        State.TransitionTo(LauncherPhase.Running);
                    }
                }

                StateChanged?.Invoke();
                return;
            }

            await Task.Delay(_readinessPollInterval, ct).ConfigureAwait(false);
        }

        lock (_sync)
        {
            if (State.CanTransitionTo(LauncherPhase.Error))
            {
                State.TransitionTo(LauncherPhase.Error, "Host/Webの起動がタイムアウトしました。");
            }
        }

        StateChanged?.Invoke();
    }

    private static async Task<bool> DefaultReadinessProbeAsync(int hostPort, CancellationToken ct)
    {
        try
        {
            using var response = await SharedHttpClient
                .GetAsync(new Uri($"http://localhost:{hostPort}/"), ct)
                .ConfigureAwait(false);
            return response.IsSuccessStatusCode;
        }
        catch (HttpRequestException)
        {
            return false;
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            return false;
        }
    }

    private void StopChildren()
    {
        lock (_sync)
        {
            TryKill(ref _hostProcess);
            TryKill(ref _webProcess);
        }
    }

    private static void TryKill(ref IChildProcess? process)
    {
        if (process is null)
        {
            return;
        }

        if (!process.HasExited)
        {
            try
            {
                process.Kill();
            }
            catch (InvalidOperationException)
            {
                // 既に終了している。
            }
        }

        process.Dispose();
        process = null;
    }

    public void Dispose()
    {
        if (_ownsJobGuard)
        {
            _jobGuard.Dispose();
        }
    }
}
