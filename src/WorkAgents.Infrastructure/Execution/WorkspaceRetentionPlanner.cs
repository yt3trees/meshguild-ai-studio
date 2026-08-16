using WorkAgents.Core;
using WorkAgents.Core.Missions;

namespace WorkAgents.Infrastructure.Execution;

/// <summary>
/// 保持期限スイープで削除すべきディレクトリを判定する純粋ロジック。
/// ファイルシステム操作から切り離してあるためユニットテストで直接検証できる。
/// </summary>
public sealed record WorkspaceDirectoryCandidate(string Path, RunRecord? Run, DateTimeOffset LastWriteTimeUtc);

public sealed record MissionWorkspaceDirectoryCandidate(
    string Path,
    Mission? Mission,
    DateTimeOffset LastWriteTimeUtc);

public static class WorkspaceRetentionPlanner
{
    private static readonly RunStatus[] TerminalStatuses =
    [
        RunStatus.Succeeded,
        RunStatus.Failed,
        RunStatus.Aborted,
    ];

    /// <summary>
    /// 削除対象のディレクトリパスを返す。対応する<see cref="RunRecord"/>が見つからない候補
    /// (フォールバック方式の<c>{agentName}\{Guid}</c>ディレクトリ等)は、実行中判定ができないため
    /// 本メソッドの対象外とし、呼び出し側で個別に扱う。
    /// </summary>
    public static IReadOnlyList<string> SelectDirectoriesToDelete(
        IReadOnlyList<WorkspaceDirectoryCandidate> candidates,
        TimeSpan retentionPeriod,
        DateTimeOffset now)
    {
        var toDelete = new List<string>();
        foreach (var candidate in candidates)
        {
            if (candidate.Run is null)
            {
                continue;
            }

            if (!TerminalStatuses.Contains(candidate.Run.Status))
            {
                // 実行中(Queued/Running/AwaitingApproval)は削除対象から除外する(FR-002)。
                continue;
            }

            var referenceTime = candidate.Run.CompletedAt ?? candidate.LastWriteTimeUtc;
            if (now - referenceTime > retentionPeriod)
            {
                toDelete.Add(candidate.Path);
            }
        }

        return toDelete;
    }

    public static IReadOnlyList<string> SelectMissionDirectoriesToDelete(
        IReadOnlyList<MissionWorkspaceDirectoryCandidate> candidates,
        TimeSpan retentionPeriod,
        DateTimeOffset now)
    {
        var toDelete = new List<string>();
        foreach (var candidate in candidates)
        {
            if (candidate.Mission is null || !IsTerminal(candidate.Mission.Status))
            {
                continue;
            }

            var referenceTime = candidate.Mission.CompletedAt ?? candidate.LastWriteTimeUtc;
            if (now - referenceTime > retentionPeriod)
            {
                toDelete.Add(candidate.Path);
            }
        }

        return toDelete;
    }

    private static bool IsTerminal(MissionStatus status)
        => status is MissionStatus.Succeeded
            or MissionStatus.NotConverged
            or MissionStatus.Failed
            or MissionStatus.Aborted;
}
