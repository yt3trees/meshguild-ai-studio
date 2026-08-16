using System.Runtime.InteropServices;

namespace WorkAgents.Tray;

/// <summary>
/// Windows Job Objectのラッパー。ランチャープロセスが正常/異常いずれの経路で終了しても、
/// 割り当てた子プロセス(Host/Web)をOSカーネルが道連れに終了させる(FR-009、research.md「5.」)。
/// </summary>
public sealed class JobObjectGuard : IDisposable
{
    private readonly nint _jobHandle;
    private bool _disposed;

    public JobObjectGuard()
    {
        _jobHandle = CreateJobObject(nint.Zero, null);
        if (_jobHandle == nint.Zero)
        {
            throw new InvalidOperationException("failed to create job object.");
        }

        var info = new JOBOBJECT_BASIC_LIMIT_INFORMATION
        {
            LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE,
        };
        var extendedInfo = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION
        {
            BasicLimitInformation = info,
        };

        var length = Marshal.SizeOf(typeof(JOBOBJECT_EXTENDED_LIMIT_INFORMATION));
        var extendedInfoPtr = Marshal.AllocHGlobal(length);
        try
        {
            Marshal.StructureToPtr(extendedInfo, extendedInfoPtr, false);
            if (!SetInformationJobObject(_jobHandle, JobObjectExtendedLimitInformation, extendedInfoPtr, (uint)length))
            {
                throw new InvalidOperationException("failed to configure job object kill-on-close limit.");
            }
        }
        finally
        {
            Marshal.FreeHGlobal(extendedInfoPtr);
        }
    }

    /// <summary>プロセスをJobへ割り当てる。ランチャー終了時に本プロセスも道連れに終了する。</summary>
    public void Assign(nint processHandle)
    {
        if (!AssignProcessToJobObject(_jobHandle, processHandle))
        {
            throw new InvalidOperationException("failed to assign process to job object.");
        }
    }

    /// <summary>実プロセスのハンドルを持つ場合のみJobへ割り当てる(テスト用フェイクはWin32Handle=nullのため無視される)。</summary>
    public void Assign(IChildProcess process)
    {
        if (process.Win32Handle is { } handle && handle != nint.Zero)
        {
            Assign(handle);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_jobHandle != nint.Zero)
        {
            CloseHandle(_jobHandle);
        }
    }

    private const int JobObjectExtendedLimitInformation = 9;
    private const uint JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x2000;

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_BASIC_LIMIT_INFORMATION
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public nuint MinimumWorkingSetSize;
        public nuint MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public nint Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IO_COUNTERS
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
    {
        public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
        public IO_COUNTERS IoInfo;
        public nuint ProcessMemoryLimit;
        public nuint JobMemoryLimit;
        public nuint PeakProcessMemoryUsed;
        public nuint PeakJobMemoryUsed;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint CreateJobObject(nint lpJobAttributes, string? lpName);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetInformationJobObject(nint hJob, int infoType, nint lpJobObjectInfo, uint cbJobObjectInfoLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AssignProcessToJobObject(nint hJob, nint hProcess);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(nint hObject);
}
