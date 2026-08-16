using System.Diagnostics;

namespace WorkAgents.Tray;

/// <summary>
/// <see cref="Process"/>を抽象化するインターフェース。<see cref="ProcessSupervisor"/>を
/// 実プロセスの起動なしに単体テストできるようにする(テストでは<c>FakeChildProcess</c>相当を注入する)。
/// </summary>
public interface IChildProcess : IDisposable
{
    bool HasExited { get; }

    /// <summary>Job Objectへの割り当てに使うOSハンドル。実プロセスなし(フェイク)の場合はnull。</summary>
    nint? Win32Handle { get; }

    event EventHandler? Exited;

    void Kill();
}

internal sealed class RealChildProcess : IChildProcess
{
    private readonly Process _process;

    public RealChildProcess(Process process)
    {
        _process = process;
        _process.Exited += (_, e) => Exited?.Invoke(this, e);
    }

    public bool HasExited => _process.HasExited;

    public nint? Win32Handle => _process.Handle;

    public event EventHandler? Exited;

    public void Kill() => _process.Kill(entireProcessTree: true);

    public void Dispose() => _process.Dispose();
}
