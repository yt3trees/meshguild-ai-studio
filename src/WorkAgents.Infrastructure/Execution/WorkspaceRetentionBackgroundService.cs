using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WorkAgents.Core.Abstractions;

namespace WorkAgents.Infrastructure.Execution;

/// <summary>
/// Run単位のワークスペースとMission単位の共有ワークスペースを保持期限に基づき削除する(FR-001〜FR-004)。
/// 対応するRun/Missionを特定できないフォールバック方式のディレクトリは、実行中判定ができないため本サービスの対象外とする。
/// </summary>
public sealed class WorkspaceRetentionBackgroundService : BackgroundService
{
    private const string CheckpointDirectoryName = "missions";

    private readonly IRunStore _runStore;
    private readonly string _workspaceRoot;
    private readonly WorkspaceRetentionOptions _options;
    private readonly IWorkspaceUsageSnapshot _snapshot;
    private readonly ILogger<WorkspaceRetentionBackgroundService> _logger;
    private readonly IMissionStore? _missionStore;
    private readonly IMissionWorkspaceStore? _missionWorkspaceStore;

    public WorkspaceRetentionBackgroundService(
        IRunStore runStore,
        string workspaceRoot,
        WorkspaceRetentionOptions options,
        IWorkspaceUsageSnapshot snapshot,
        ILogger<WorkspaceRetentionBackgroundService> logger,
        IMissionStore? missionStore = null,
        IMissionWorkspaceStore? missionWorkspaceStore = null)
    {
        _runStore = runStore;
        _workspaceRoot = workspaceRoot;
        _options = options;
        _snapshot = snapshot;
        _logger = logger;
        _missionStore = missionStore;
        _missionWorkspaceStore = missionWorkspaceStore;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            return;
        }

        using var timer = new PeriodicTimer(_options.SweepInterval);
        try
        {
            do
            {
                try
                {
                    await TickAsync(DateTimeOffset.UtcNow, stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(ex, "workspace retention sweep failed");
                }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    public async Task<WorkspaceRetentionSweepResult> TickAsync(DateTimeOffset now, CancellationToken ct = default)
    {
        var sweepStarted = now;
        var candidates = new List<WorkspaceDirectoryCandidate>();
        var missionCandidates = new List<MissionWorkspaceDirectoryCandidate>();

        if (Directory.Exists(_workspaceRoot))
        {
            foreach (var dir in Directory.EnumerateDirectories(_workspaceRoot))
            {
                var name = Path.GetFileName(dir);
                if (string.Equals(name, CheckpointDirectoryName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var run = await _runStore.GetAsync(name, ct);
                var lastWrite = new DateTimeOffset(Directory.GetLastWriteTimeUtc(dir), TimeSpan.Zero);
                candidates.Add(new WorkspaceDirectoryCandidate(dir, run, lastWrite));
            }

            if (_missionStore is not null && _missionWorkspaceStore is not null)
            {
                var missionsRoot = Path.Combine(_workspaceRoot, CheckpointDirectoryName);
                if (Directory.Exists(missionsRoot))
                {
                    foreach (var missionDirectory in Directory.EnumerateDirectories(missionsRoot))
                    {
                        var workDirectory = Path.Combine(missionDirectory, "work");
                        var missionId = Path.GetFileName(missionDirectory);
                        var mission = await _missionStore.GetAsync(missionId, ct);
                        var record = await _missionWorkspaceStore.GetAsync(missionId, ct);
                        var checkpointsDirectory = Path.Combine(missionDirectory, "checkpoints");
                        if (mission is null || record is null
                            || (!Directory.Exists(workDirectory) && !Directory.Exists(checkpointsDirectory)))
                        {
                            continue;
                        }

                        var lastWrite = new DateTimeOffset(Directory.GetLastWriteTimeUtc(missionDirectory), TimeSpan.Zero);
                        missionCandidates.Add(new MissionWorkspaceDirectoryCandidate(missionDirectory, mission, lastWrite));
                    }
                }
            }
        }

        var toDelete = WorkspaceRetentionPlanner.SelectDirectoriesToDelete(candidates, _options.RetentionPeriod, now);
        var missionToDelete = WorkspaceRetentionPlanner.SelectMissionDirectoriesToDelete(
            missionCandidates,
            _options.RetentionPeriod,
            now);
        var missionIdsByPath = missionCandidates.ToDictionary(
            candidate => candidate.Path,
            candidate => Path.GetFileName(candidate.Path),
            StringComparer.OrdinalIgnoreCase);
        var allToDelete = toDelete.Concat(missionToDelete).ToArray();

        var deletedCount = 0;
        var failedCount = 0;
        long bytesFreed = 0;
        foreach (var path in allToDelete)
        {
            try
            {
                var size = CalculateDirectorySize(path);
                Directory.Delete(path, recursive: true);
                deletedCount++;
                bytesFreed += size;
                if (_missionWorkspaceStore is not null && missionIdsByPath.TryGetValue(path, out var missionId))
                {
                    await _missionWorkspaceStore.MarkDeletedAsync(missionId, now, ct);
                }
            }
            catch (Exception ex)
            {
                failedCount++;
                _logger.LogWarning(ex, "failed to delete workspace directory {Path}", path);
            }
        }

        var result = new WorkspaceRetentionSweepResult
        {
            SweepStartedAtUtc = sweepStarted,
            SweepFinishedAtUtc = DateTimeOffset.UtcNow,
            EvaluatedCount = candidates.Count + missionCandidates.Count,
            DeletedCount = deletedCount,
            BytesFreed = bytesFreed,
            FailedCount = failedCount,
        };
        _snapshot.RecordSweep(result);

        if (deletedCount > 0 || failedCount > 0)
        {
            _logger.LogInformation(
                "workspace retention sweep evaluated={Evaluated} deleted={Deleted} bytesFreed={BytesFreed} failed={Failed}",
                result.EvaluatedCount, result.DeletedCount, result.BytesFreed, result.FailedCount);
        }

        return result;
    }

    private static long CalculateDirectorySize(string path)
    {
        long total = 0;
        foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
        {
            try
            {
                total += new FileInfo(file).Length;
            }
            catch (IOException)
            {
                // サイズ集計中にファイルが変化しても致命的ではないため無視する。
            }
        }

        return total;
    }
}
