using WorkAgents.Core;

namespace WorkAgents.Infrastructure.Execution;

/// <summary>`GET /workspace/usage` の応答を組み立てる(FR-003、contracts/artifacts-api.md)。</summary>
public sealed class WorkspaceUsageReportBuilder
{
    private readonly ProfileOptions _profileOptions;
    private readonly WorkspaceRetentionOptions _retentionOptions;
    private readonly IWorkspaceUsageSnapshot _snapshot;

    public WorkspaceUsageReportBuilder(ProfileOptions profileOptions, WorkspaceRetentionOptions retentionOptions, IWorkspaceUsageSnapshot snapshot)
    {
        _profileOptions = profileOptions;
        _retentionOptions = retentionOptions;
        _snapshot = snapshot;
    }

    public WorkspaceUsageReport Build()
    {
        long totalBytes = 0;
        var directoryCount = 0;
        if (Directory.Exists(_profileOptions.WorkspaceRoot))
        {
            foreach (var dir in Directory.EnumerateDirectories(_profileOptions.WorkspaceRoot))
            {
                if (string.Equals(Path.GetFileName(dir), "missions", StringComparison.OrdinalIgnoreCase))
                {
                    foreach (var missionDir in Directory.EnumerateDirectories(dir))
                    {
                        directoryCount++;
                        foreach (var file in EnumerateFilesSafely(missionDir))
                        {
                            totalBytes += TryGetLength(file);
                        }
                    }
                    continue;
                }

                directoryCount++;
                foreach (var file in EnumerateFilesSafely(dir))
                {
                    totalBytes += TryGetLength(file);
                }
            }
        }

        return new WorkspaceUsageReport(totalBytes, directoryCount, _retentionOptions.RetentionPeriod.Days, _snapshot.LastSweep);
    }

    private static IEnumerable<string> EnumerateFilesSafely(string dir)
    {
        try
        {
            return Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories);
        }
        catch (IOException)
        {
            return [];
        }
    }

    private static long TryGetLength(string file)
    {
        try
        {
            return new FileInfo(file).Length;
        }
        catch (IOException)
        {
            return 0;
        }
    }
}

public sealed record WorkspaceUsageReport(
    long TotalBytes,
    int DirectoryCount,
    int RetentionPeriodDays,
    WorkspaceRetentionSweepResult? LastSweep);
