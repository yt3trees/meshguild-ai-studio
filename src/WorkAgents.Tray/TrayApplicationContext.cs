using System.Diagnostics;

namespace WorkAgents.Tray;

/// <summary>
/// トレイUI本体。<see cref="System.Windows.Forms.NotifyIcon"/>と<see cref="ContextMenuStrip"/>を
/// 構築し、contracts/tray-menu-contract.mdのメニュー項目・アイコン状態表示・確認ダイアログを配線する。
/// UIに依存するため単体テスト対象外とし、quickstart.mdの手動シナリオで検証する(plan.md Testing方針)。
/// </summary>
public sealed class TrayApplicationContext : ApplicationContext
{
    private readonly ProcessSupervisor _supervisor;
    private readonly NotifyIcon _notifyIcon;
    private readonly ContextMenuStrip _menu;
    private readonly ToolStripMenuItem _openMenuItem;
    private readonly ToolStripMenuItem _updateMenuItem;
    private readonly ToolStripMenuItem _exitMenuItem;
    private readonly ToolStripMenuItem _settingsMenuItem;
    private readonly TrayIconSet _iconSet;
    private readonly RunActivityChecker _runActivityChecker = new();

    public TrayApplicationContext(ProcessSupervisor supervisor)
    {
        _supervisor = supervisor;
        _iconSet = TrayIconSet.LoadDefault();

        _openMenuItem = new ToolStripMenuItem("開く", null, OnOpenClicked);
        _updateMenuItem = new ToolStripMenuItem("更新", null, OnUpdateClicked);
        _settingsMenuItem = new ToolStripMenuItem("設定", null, OnSettingsClicked);
        _exitMenuItem = new ToolStripMenuItem("終了", null, OnExitClicked);

        _menu = new ContextMenuStrip();
        _menu.Items.Add(_openMenuItem);
        _menu.Items.Add(_updateMenuItem);
        _menu.Items.Add(_settingsMenuItem);
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(_exitMenuItem);

        _notifyIcon = new NotifyIcon
        {
            Icon = _iconSet.Starting,
            Text = "WorkAgents (起動中)",
            ContextMenuStrip = _menu,
            Visible = true,
        };
        _notifyIcon.DoubleClick += OnOpenClicked;

        _supervisor.StateChanged += OnSupervisorStateChanged;
        RefreshTrayVisual();
    }

    private void OnSupervisorStateChanged()
    {
        if (_notifyIcon.ContextMenuStrip?.InvokeRequired == true)
        {
            _notifyIcon.ContextMenuStrip.Invoke(RefreshTrayVisual);
            return;
        }

        RefreshTrayVisual();
    }

    /// <summary>contracts/tray-menu-contract.mdの「トレイアイコンの状態表示契約」に対応する。</summary>
    private void RefreshTrayVisual()
    {
        var phase = _supervisor.State.Phase;
        _notifyIcon.Icon = phase switch
        {
            LauncherPhase.Starting => _iconSet.Starting,
            LauncherPhase.Running => _iconSet.Running,
            LauncherPhase.Updating => _iconSet.Updating,
            LauncherPhase.Error => _iconSet.Error,
            LauncherPhase.Exiting => _iconSet.Starting,
            _ => _iconSet.Starting,
        };

        _notifyIcon.Text = phase switch
        {
            LauncherPhase.Starting => "WorkAgents (起動中)",
            LauncherPhase.Running => "WorkAgents (稼働中)",
            LauncherPhase.Updating => "WorkAgents (更新中)",
            LauncherPhase.Error => $"WorkAgents (エラー: {_supervisor.State.ErrorMessage})",
            LauncherPhase.Exiting => "WorkAgents (終了中)",
            _ => "WorkAgents",
        };

        // FR-014: 更新中は「更新」の多重実行を防ぐため無効化する。
        _updateMenuItem.Enabled = phase is LauncherPhase.Running or LauncherPhase.Error;
        _openMenuItem.Enabled = phase is LauncherPhase.Running or LauncherPhase.Starting or LauncherPhase.Error;
    }

    /// <summary>FR-016: 二重起動を検知した既存インスタンス側から呼ばれ、バルーン通知で目立たせる。</summary>
    public void NotifyDuplicateLaunchAttempt()
    {
        if (_notifyIcon.ContextMenuStrip?.InvokeRequired == true)
        {
            _notifyIcon.ContextMenuStrip.Invoke(NotifyDuplicateLaunchAttempt);
            return;
        }

        _notifyIcon.ShowBalloonTip(
            3000,
            "WorkAgentsは既に起動しています",
            "タスクトレイのアイコンから操作してください。",
            ToolTipIcon.Info);
    }

    /// <summary>「開く」(FR-003, FR-004): 稼働中のみ既定ブラウザでWeb UIを開く。起動中/エラー中は案内のみ行う。</summary>
    private void OnOpenClicked(object? sender, EventArgs e)
    {
        var phase = _supervisor.State.Phase;
        if (phase is LauncherPhase.Starting or LauncherPhase.Updating)
        {
            _notifyIcon.ShowBalloonTip(2000, "WorkAgents", "起動中です。しばらくお待ちください。", ToolTipIcon.Info);
            return;
        }

        if (phase == LauncherPhase.Error)
        {
            _notifyIcon.ShowBalloonTip(2000, "WorkAgents", "エラー状態のため開けません。「更新」を試してください。", ToolTipIcon.Warning);
            return;
        }

        var url = $"http://localhost:{_supervisor.Settings.WebPort}/";
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }

    /// <summary>「更新」(FR-005〜FR-007): 進行中Runがあれば確認ダイアログを経てからプロセスを再起動する。</summary>
    private async void OnUpdateClicked(object? sender, EventArgs e)
    {
        var hasActiveRuns = await _runActivityChecker.HasActiveRunsAsync(_supervisor.Settings.HostPort);
        if (hasActiveRuns && !ConfirmationDialog.ConfirmActiveRunInterruption("更新"))
        {
            return;
        }

        await _supervisor.RestartAsync();
    }

    /// <summary>「終了」(FR-007, FR-008): 進行中Runがあれば確認ダイアログを経てからHost/Webを終了する。</summary>
    private async void OnExitClicked(object? sender, EventArgs e)
    {
        var hasActiveRuns = await _runActivityChecker.HasActiveRunsAsync(_supervisor.Settings.HostPort);
        if (hasActiveRuns && !ConfirmationDialog.ConfirmActiveRunInterruption("終了"))
        {
            return;
        }

        await _supervisor.ShutdownAsync();
        ExitThread();
    }

    /// <summary>「設定」(FR-010〜FR-012): ポート設定ダイアログをモーダル表示する。</summary>
    private void OnSettingsClicked(object? sender, EventArgs e)
    {
        var window = new SettingsWindow(_supervisor.Settings);
        window.ShowDialog();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _supervisor.StateChanged -= OnSupervisorStateChanged;
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            _menu.Dispose();
            _iconSet.Dispose();
        }

        base.Dispose(disposing);
    }
}
