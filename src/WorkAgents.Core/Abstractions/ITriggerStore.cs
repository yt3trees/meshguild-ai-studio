using WorkAgents.Core.Triggers;

namespace WorkAgents.Core.Abstractions;

/// <summary>TriggerDefinition / TriggerFire の永続化抽象 (triggers / trigger_fires テーブル)。</summary>
public interface ITriggerStore
{
    Task CreateAsync(TriggerDefinition trigger, CancellationToken ct = default);

    Task<TriggerDefinition?> GetAsync(string name, CancellationToken ct = default);

    Task<IReadOnlyList<TriggerDefinition>> ListAsync(CancellationToken ct = default);

    Task UpdateAsync(TriggerDefinition trigger, CancellationToken ct = default);

    Task DeleteAsync(string name, CancellationToken ct = default);

    Task SetEnabledAsync(string name, bool enabled, CancellationToken ct = default);

    Task RecordFireAsync(TriggerFire fire, CancellationToken ct = default);

    Task<IReadOnlyList<TriggerFire>> ListFiresAsync(string triggerId, CancellationToken ct = default);
}
