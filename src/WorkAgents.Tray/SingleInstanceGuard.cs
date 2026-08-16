namespace WorkAgents.Tray;

/// <summary>
/// 名前付きMutexによる二重起動検知(FR-016)。取得成否の判定のみを担い、
/// 「既存インスタンスを目立たせる」通知(Program.cs/TrayApplicationContext.cs側の責務)とは分離する。
/// </summary>
public sealed class SingleInstanceGuard : IDisposable
{
    private readonly Mutex _mutex;
    private bool _disposed;

    public SingleInstanceGuard(string mutexName)
    {
        _mutex = new Mutex(initiallyOwned: true, name: mutexName, out var createdNew);
        IsPrimaryInstance = createdNew;
    }

    /// <summary>trueなら本プロセスが最初の(唯一の)インスタンスであることを意味する。</summary>
    public bool IsPrimaryInstance { get; }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (IsPrimaryInstance)
        {
            _mutex.ReleaseMutex();
        }

        _mutex.Dispose();
    }
}
