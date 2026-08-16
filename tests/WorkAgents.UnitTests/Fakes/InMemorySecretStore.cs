using WorkAgents.Core.Abstractions;

namespace WorkAgents.UnitTests.Fakes;

/// <summary>テスト用のインメモリ ISecretStore。実 API キーやDPAPIに依存しない。</summary>
public sealed class InMemorySecretStore : ISecretStore
{
    private readonly Dictionary<string, string> _values = new(StringComparer.Ordinal);

    public Task<string?> GetAsync(string name, CancellationToken ct = default)
        => Task.FromResult(_values.TryGetValue(name, out var value) ? value : null);

    public Task SetAsync(string name, string value, CancellationToken ct = default)
    {
        _values[name] = value;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<string>> ListAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<string>>(_values.Keys.ToList());

    public Task<bool> DeleteAsync(string name, CancellationToken ct = default)
        => Task.FromResult(_values.Remove(name));
}
