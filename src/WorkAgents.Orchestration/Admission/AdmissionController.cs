using WorkAgents.Core.Abstractions;
using WorkAgents.Core.Missions;

namespace WorkAgents.Orchestration.Admission;

/// <summary>ミッション受付の結果。</summary>
public sealed record MissionAdmission(bool Admitted, int? QueuePosition, MissionQueuedReason? Reason);

/// <summary>エージェントインスタンス枠の受付結果。</summary>
public sealed record AgentAdmission(bool Admitted, int? QueuePosition);

/// <summary>
/// グローバル上限 (同時ミッション 5 件、同時稼働エージェント 12 体) を一元管理する (T039)。
/// ミッションの待機列は <see cref="IMissionQueueStore"/> へ永続化する (FR-057、FR-058)。
/// エージェント枠は実行中ミッションに紐づく短命な待機のため、プロセス内 FIFO で管理する。
/// </summary>
public sealed class AdmissionController
{
    private readonly IMissionQueueStore _missionQueueStore;
    private readonly int _maxConcurrentMissions;
    private readonly int _maxConcurrentAgents;
    private readonly object _gate = new();

    private int _activeMissions;
    private int _activeAgents;
    private readonly List<(string MissionId, string RequestId)> _agentWaitQueue = new();

    public AdmissionController(
        IMissionQueueStore missionQueueStore,
        int maxConcurrentMissions = 5,
        int maxConcurrentAgents = 12)
    {
        ArgumentNullException.ThrowIfNull(missionQueueStore);
        if (maxConcurrentMissions < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxConcurrentMissions));
        }
        if (maxConcurrentAgents < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxConcurrentAgents));
        }

        _missionQueueStore = missionQueueStore;
        _maxConcurrentMissions = maxConcurrentMissions;
        _maxConcurrentAgents = maxConcurrentAgents;
    }

    /// <summary>ミッションの実行枠を要求する。上限に達していれば待機列へ永続化して待機理由を返す。</summary>
    public async Task<MissionAdmission> RequestMissionAsync(string missionId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(missionId);

        var admit = false;
        lock (_gate)
        {
            if (_activeMissions < _maxConcurrentMissions)
            {
                _activeMissions++;
                admit = true;
            }
        }

        if (admit)
        {
            return new MissionAdmission(true, null, null);
        }

        var position = await _missionQueueStore.EnqueueAsync(missionId, MissionQueuedReason.ConcurrencyLimit, ct);
        return new MissionAdmission(false, position, MissionQueuedReason.ConcurrencyLimit);
    }

    /// <summary>ミッションの実行枠を解放し、待機列の先頭 (最も古い待機) を昇格させる。昇格したミッション ID を返す。</summary>
    public async Task<IReadOnlyList<string>> ReleaseMissionAsync(string missionId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(missionId);

        lock (_gate)
        {
            if (_activeMissions > 0)
            {
                _activeMissions--;
            }
        }

        var promoted = new List<string>();
        var next = await _missionQueueStore.DequeueAsync(ct);
        if (next is not null)
        {
            lock (_gate)
            {
                _activeMissions++;
            }
            promoted.Add(next.MissionId);
        }

        return promoted;
    }

    /// <summary>エージェントインスタンスの実行枠を要求する。上限に達していればプロセス内待機列へ入れる。</summary>
    public AgentAdmission RequestAgent(string missionId, string requestId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(missionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(requestId);

        lock (_gate)
        {
            if (_activeAgents < _maxConcurrentAgents)
            {
                _activeAgents++;
                return new AgentAdmission(true, null);
            }

            _agentWaitQueue.Add((missionId, requestId));
            return new AgentAdmission(false, _agentWaitQueue.Count);
        }
    }

    /// <summary>エージェントインスタンスの実行枠を解放し、待機列の先頭を昇格させる。昇格した要求 ID を返す。</summary>
    public IReadOnlyList<string> ReleaseAgent(string requestId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestId);

        lock (_gate)
        {
            if (_activeAgents > 0)
            {
                _activeAgents--;
            }

            var promoted = new List<string>();
            if (_agentWaitQueue.Count > 0)
            {
                var next = _agentWaitQueue[0];
                _agentWaitQueue.RemoveAt(0);
                _activeAgents++;
                promoted.Add(next.RequestId);
            }

            return promoted;
        }
    }

    /// <summary>待機列からミッションを取り除く (昇格を待たずに中断された場合)。</summary>
    public Task RemoveFromQueueAsync(string missionId, CancellationToken ct = default)
        => _missionQueueStore.RemoveAsync(missionId, ct);

    /// <summary>
    /// 容量に余裕があれば待機列の先頭を 1 件だけ取り出して昇格させる (T041 の再走査で使う)。
    /// 昇格できなければ null。
    /// </summary>
    public async Task<string?> TryPromoteFromQueueAsync(CancellationToken ct = default)
    {
        bool hasCapacity;
        lock (_gate)
        {
            hasCapacity = _activeMissions < _maxConcurrentMissions;
        }

        if (!hasCapacity)
        {
            return null;
        }

        var next = await _missionQueueStore.DequeueAsync(ct);
        if (next is null)
        {
            return null;
        }

        lock (_gate)
        {
            _activeMissions++;
        }

        return next.MissionId;
    }

    public int ActiveMissions
    {
        get { lock (_gate) { return _activeMissions; } }
    }

    public int ActiveAgents
    {
        get { lock (_gate) { return _activeAgents; } }
    }
}
