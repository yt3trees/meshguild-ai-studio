namespace WorkAgents.Tray;

internal static class Program
{
    private const string SingleInstanceMutexName = "Global\\WorkAgents.Tray.SingleInstance";
    private const string DuplicateLaunchEventName = "Global\\WorkAgents.Tray.DuplicateLaunchSignal";

    [STAThread]
    private static void Main()
    {
        using var singleInstanceGuard = new SingleInstanceGuard(SingleInstanceMutexName);
        if (!singleInstanceGuard.IsPrimaryInstance)
        {
            // FR-016: 二重起動時は新規プロセスは何もせず終了し、既存インスタンスへ通知するだけに留める。
            NotifyExistingInstance();
            return;
        }

        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        var settings = LauncherSettings.Load(LauncherSettings.GetDefaultFilePath());
        var baseDirectory = AppContext.BaseDirectory;
        var hostExecutablePath = ResolveExecutablePath(baseDirectory, "WorkAgents.Host");
        var webExecutablePath = ResolveExecutablePath(baseDirectory, "WorkAgents.Web");

        using var supervisor = new ProcessSupervisor(settings, hostExecutablePath, webExecutablePath);
        using var context = new TrayApplicationContext(supervisor);

        ListenForDuplicateLaunchSignal(context);

        _ = supervisor.StartAsync();
        Application.Run(context);
    }

    /// <summary>
    /// フォルダ配布(同一ディレクトリに全exeを配置)と、プロジェクト単位のpublish出力
    /// (兄弟ディレクトリにHost/Webがある構成)の両方を素朴に試す。
    /// </summary>
    private static string ResolveExecutablePath(string baseDirectory, string projectName)
    {
        var sameDirectory = Path.Combine(baseDirectory, $"{projectName}.exe");
        if (File.Exists(sameDirectory))
        {
            return sameDirectory;
        }

        return Path.GetFullPath(Path.Combine(baseDirectory, "..", projectName, $"{projectName}.exe"));
    }

    private static void NotifyExistingInstance()
    {
        try
        {
            using var signal = EventWaitHandle.OpenExisting(DuplicateLaunchEventName);
            signal.Set();
        }
        catch (WaitHandleCannotBeOpenedException)
        {
            // 既存インスタンス側がまだイベントを作成していないタイミング。通知は諦めて終了する。
        }
    }

    private static void ListenForDuplicateLaunchSignal(TrayApplicationContext context)
    {
        var signal = new EventWaitHandle(false, EventResetMode.AutoReset, DuplicateLaunchEventName);
        var thread = new Thread(() =>
        {
            while (true)
            {
                signal.WaitOne();
                context.NotifyDuplicateLaunchAttempt();
            }
        })
        {
            IsBackground = true,
        };
        thread.Start();
    }
}
