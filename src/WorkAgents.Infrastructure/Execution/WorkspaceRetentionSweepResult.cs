namespace WorkAgents.Infrastructure.Execution;

/// <summary>1回分の保持期限スイープの結果(FR-004)。</summary>
public sealed record WorkspaceRetentionSweepResult
{
    public DateTimeOffset SweepStartedAtUtc { get; init; }

    public DateTimeOffset SweepFinishedAtUtc { get; init; }

    public int EvaluatedCount { get; init; }

    public int DeletedCount { get; init; }

    public long BytesFreed { get; init; }

    public int FailedCount { get; init; }
}

/// <summary>直近のスイープ結果とワークスペース使用状況を保持する(`GET /workspace/usage` から参照)。</summary>
public interface IWorkspaceUsageSnapshot
{
    WorkspaceRetentionSweepResult? LastSweep { get; }

    void RecordSweep(WorkspaceRetentionSweepResult result);
}

public sealed class WorkspaceUsageSnapshot : IWorkspaceUsageSnapshot
{
    private WorkspaceRetentionSweepResult? _lastSweep;

    public WorkspaceRetentionSweepResult? LastSweep => Volatile.Read(ref _lastSweep);

    public void RecordSweep(WorkspaceRetentionSweepResult result) => Volatile.Write(ref _lastSweep, result);
}
