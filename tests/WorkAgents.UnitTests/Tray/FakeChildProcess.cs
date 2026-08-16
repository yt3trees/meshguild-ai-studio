using System.Diagnostics;
using WorkAgents.Tray;

namespace WorkAgents.UnitTests.Tray;

/// <summary>実プロセスを起動せずに<see cref="ProcessSupervisor"/>を単体テストするためのフェイク。</summary>
internal sealed class FakeChildProcess : IChildProcess
{
    public bool HasExited { get; private set; }

    public nint? Win32Handle => null;

    public event EventHandler? Exited;

    public bool Killed { get; private set; }

    public void Kill()
    {
        Killed = true;
        SimulateExit();
    }

    public void SimulateExit()
    {
        if (HasExited)
        {
            return;
        }

        HasExited = true;
        Exited?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
    }
}

internal sealed class FakeChildProcessLauncher : IChildProcessLauncher
{
    public List<FakeChildProcess> Started { get; } = [];

    public List<ProcessStartInfo> StartInfos { get; } = [];

    public IChildProcess Start(ProcessStartInfo startInfo)
    {
        var process = new FakeChildProcess();
        Started.Add(process);
        StartInfos.Add(startInfo);
        return process;
    }
}
