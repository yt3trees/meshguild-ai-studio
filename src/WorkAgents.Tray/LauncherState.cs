namespace WorkAgents.Tray;

/// <summary>ランチャー常駐プロセスの現在フェーズ(data-model.md「LauncherState」参照)。</summary>
public enum LauncherPhase
{
    Starting,
    Running,
    Updating,
    Exiting,
    Error,
}

/// <summary>
/// トレイランチャーの状態機械。data-model.mdの遷移表に定義された遷移のみを許可し、
/// 不正な遷移(<see cref="LauncherPhase.Updating"/>中の多重「更新」等、FR-014)を拒否する。
/// UIやプロセス起動に依存しないため単体テスト可能。
/// </summary>
public sealed class LauncherState
{
    private static readonly Dictionary<LauncherPhase, LauncherPhase[]> AllowedTransitions = new()
    {
        [LauncherPhase.Starting] = [LauncherPhase.Running, LauncherPhase.Error],
        [LauncherPhase.Running] = [LauncherPhase.Updating, LauncherPhase.Exiting, LauncherPhase.Error],
        [LauncherPhase.Updating] = [LauncherPhase.Running, LauncherPhase.Error],
        [LauncherPhase.Error] = [LauncherPhase.Updating, LauncherPhase.Exiting],
        [LauncherPhase.Exiting] = [],
    };

    public LauncherPhase Phase { get; private set; } = LauncherPhase.Starting;

    public string? ErrorMessage { get; private set; }

    public bool CanTransitionTo(LauncherPhase target) => AllowedTransitions[Phase].Contains(target);

    /// <summary>許可されていない遷移は<see cref="InvalidOperationException"/>を送出して拒否する。</summary>
    public void TransitionTo(LauncherPhase target, string? errorMessage = null)
    {
        if (!CanTransitionTo(target))
        {
            throw new InvalidOperationException($"cannot transition from {Phase} to {target}.");
        }

        Phase = target;
        ErrorMessage = target == LauncherPhase.Error ? errorMessage : null;
    }
}
