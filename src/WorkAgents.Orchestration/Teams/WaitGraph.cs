namespace WorkAgents.Orchestration.Teams;

/// <summary>Result of adding a wait dependency.</summary>
public sealed record WaitRegistration(bool Accepted, bool CycleDetected, IReadOnlyList<string> Cycle)
{
    public static WaitRegistration AcceptedResult() => new(true, false, Array.Empty<string>());

    public static WaitRegistration CycleResult(IReadOnlyList<string> cycle) => new(false, true, cycle);
}

/// <summary>Tracks agent reply dependencies and detects deadlocks before waiting.</summary>
public sealed class WaitGraph
{
    private readonly Dictionary<string, string> _waits = new(StringComparer.Ordinal);
    private readonly object _gate = new();

    public WaitRegistration Register(string waitingInstanceId, string awaitedInstanceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(waitingInstanceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(awaitedInstanceId);

        lock (_gate)
        {
            _waits[waitingInstanceId] = awaitedInstanceId;
            var cycle = FindCycleFrom(waitingInstanceId);
            if (cycle.Count > 0)
            {
                _waits.Remove(waitingInstanceId);
                return WaitRegistration.CycleResult(cycle);
            }

            return WaitRegistration.AcceptedResult();
        }
    }

    public WaitRegistration AddWait(string waitingInstanceId, string awaitedInstanceId)
        => Register(waitingInstanceId, awaitedInstanceId);

    public void Remove(string waitingInstanceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(waitingInstanceId);
        lock (_gate)
        {
            _waits.Remove(waitingInstanceId);
        }
    }

    public bool WouldCreateCycle(string waitingInstanceId, string awaitedInstanceId)
    {
        lock (_gate)
        {
            var previous = _waits.TryGetValue(waitingInstanceId, out var value) ? value : null;
            _waits[waitingInstanceId] = awaitedInstanceId;
            var result = FindCycleFrom(waitingInstanceId).Count > 0;
            if (previous is null)
            {
                _waits.Remove(waitingInstanceId);
            }
            else
            {
                _waits[waitingInstanceId] = previous;
            }
            return result;
        }
    }

    public bool HasCycle()
    {
        lock (_gate)
        {
            return _waits.Keys.Any(key => FindCycleFrom(key).Count > 0);
        }
    }

    public IReadOnlyDictionary<string, string> Snapshot()
    {
        lock (_gate)
        {
            return new Dictionary<string, string>(_waits, StringComparer.Ordinal);
        }
    }

    private List<string> FindCycleFrom(string start)
    {
        var path = new List<string>();
        var indexByNode = new Dictionary<string, int>(StringComparer.Ordinal);
        var current = start;
        while (true)
        {
            if (indexByNode.TryGetValue(current, out var index))
            {
                var cycle = path.Skip(index).ToList();
                cycle.Add(current);
                return cycle;
            }

            indexByNode[current] = path.Count;
            path.Add(current);
            if (!_waits.TryGetValue(current, out current!))
            {
                return new List<string>();
            }
        }
    }
}
