using Microsoft.Extensions.Logging;

namespace WorkAgents.Harness.GitAuth;

/// <summary>
/// <see cref="IGitAuth"/> の既定実装(5.5)。token 発行と git-credentials 書き込みを協調させる。
/// <see cref="InitializeAsync"/> は token を取得しファイルに書き込み、<see cref="RefreshAsync"/> は
/// 有効期限前に再取得する(無条件で上書き)。本クラスは token 文字列を一切ログ/プロパティに出さない。
/// </summary>
public sealed class GitAuthenticator : IGitAuth
{
    private readonly IInstallationTokenSource _tokenSource;
    private readonly GitCredentialStoreInitializer _writer;
    private readonly ILogger<GitAuthenticator>? _logger;
    private readonly Lock _gate = new();
    private InstallationToken? _current;

    public GitAuthenticator(
        IInstallationTokenSource tokenSource,
        GitCredentialStoreInitializer writer,
        ILogger<GitAuthenticator>? logger = null)
    {
        _tokenSource = tokenSource;
        _writer = writer;
        _logger = logger;
    }

    public DateTimeOffset? TokenExpiresAt
    {
        get { lock (_gate) { return _current?.ExpiresAt; } }
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var token = await _tokenSource.MintAsync(cancellationToken);
        lock (_gate) { _current = token; }
        await _writer.WriteTokenAsync(token, cancellationToken);
        _logger?.LogInformation("git auth initialized: credential store={Path}", _writer.CredentialFilePath);
    }

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        // 無条件再発行(git-credentials は上書きするだけ・冪等)。
        var token = await _tokenSource.MintAsync(cancellationToken);
        lock (_gate) { _current = token; }
        await _writer.WriteTokenAsync(token, cancellationToken);
        _logger?.LogInformation("git auth refreshed (credential store overwritten).");
    }
}