using WorkAgents.Core.Missions;

namespace WorkAgents.Core.Abstractions;

/// <summary>待機列の 1 件 (data-model.md MissionQueue)。</summary>
public sealed record MissionQueueEntry
{
    public required string MissionId { get; init; }

    public required int Position { get; init; }

    public required MissionQueuedReason Reason { get; init; }

    public DateTimeOffset EnqueuedAt { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>MissionQueue の永続化抽象 (mission_queue テーブル、FIFO)。</summary>
public interface IMissionQueueStore
{
    /// <summary>末尾へ追加し、確定した position を返す。</summary>
    Task<int> EnqueueAsync(string missionId, MissionQueuedReason reason, CancellationToken ct = default);

    Task<IReadOnlyList<MissionQueueEntry>> ListAsync(CancellationToken ct = default);

    /// <summary>先頭 (最小 position) を取り除く。無ければ null。</summary>
    Task<MissionQueueEntry?> DequeueAsync(CancellationToken ct = default);

    Task RemoveAsync(string missionId, CancellationToken ct = default);
}
