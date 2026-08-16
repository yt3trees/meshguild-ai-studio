using WorkAgents.Core.Missions;

namespace WorkAgents.Core.Abstractions;

/// <summary>Mission budget persistence and usage updates.</summary>
public interface IBudgetStore
{
    Task UpsertAsync(Budget budget, CancellationToken ct = default);

    Task<Budget?> GetAsync(string missionId, CancellationToken ct = default);
}
