using System.Diagnostics;

namespace WorkAgents.Tray;

/// <summary>子プロセスの起動を抽象化する(<see cref="ProcessSupervisor"/>のテスト容易性のため)。</summary>
public interface IChildProcessLauncher
{
    IChildProcess Start(ProcessStartInfo startInfo);
}

internal sealed class RealChildProcessLauncher : IChildProcessLauncher
{
    public IChildProcess Start(ProcessStartInfo startInfo)
    {
        var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        process.Start();
        return new RealChildProcess(process);
    }
}
