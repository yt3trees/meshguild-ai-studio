namespace WorkAgents.Agents.Tools;

/// <summary>
/// チーム固有ツールが到達してよいホストを判定する(FR-009)。ツールプロバイダ実装は、外部リソース
/// (社内APIなど)へアクセスする直前にこのサービスを呼び出し、allowlist外への到達を拒否できる。
/// <c>Agents:ToolPlugins:AllowedHosts</c> が空の場合は制限なし(既存挙動との後方互換)。
/// </summary>
public interface IToolPluginHostAllowlist
{
    bool IsAllowed(string host);

    /// <summary>許可されていない場合に <see cref="InvalidOperationException"/> を送出する。</summary>
    void EnsureAllowed(string host);
}

public sealed class ToolPluginHostAllowlist : IToolPluginHostAllowlist
{
    private readonly HashSet<string> _allowedHosts;

    public ToolPluginHostAllowlist(IReadOnlyList<string> allowedHosts)
    {
        ArgumentNullException.ThrowIfNull(allowedHosts);
        _allowedHosts = new HashSet<string>(allowedHosts, StringComparer.OrdinalIgnoreCase);
    }

    public bool IsAllowed(string host)
        => _allowedHosts.Count == 0 || (!string.IsNullOrWhiteSpace(host) && _allowedHosts.Contains(host));

    public void EnsureAllowed(string host)
    {
        if (!IsAllowed(host))
        {
            throw new InvalidOperationException(
                $"Tool plugin access to host '{host}' is not allowed. Add it to Agents:ToolPlugins:AllowedHosts to permit access.");
        }
    }
}
