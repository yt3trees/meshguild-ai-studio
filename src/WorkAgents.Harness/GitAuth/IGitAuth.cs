namespace WorkAgents.Harness.GitAuth;

/// <summary>
/// Git 認証の初期化・更新を担う(5.5)。<see cref="InitializeAsync"/> で GitHub App installation token を発行し、
/// git credential 層(~/.git-credentials + credential.helper store)に仕込む。以降エージェントは
/// トークン無し URL で <c>git clone</c> するだけで認証が透過的に効く(鉄則:トークンを LLM に見せない)。
/// </summary>
public interface IGitAuth
{
    /// <summary>token 発行 + git-credentials 書き込み + git config 設定。エージェント実行より前に1回呼ぶ。</summary>
    Task InitializeAsync(CancellationToken cancellationToken = default);

    /// <summary>installation token の再有効期限時刻(UTC)。長時間 run での再発行判断用(M3 で活用)。</summary>
    DateTimeOffset? TokenExpiresAt { get; }

    /// <summary>token が失効近くな場合に再発行し git-credentials を上書きする(git-credentials は上書きするだけ)。</summary>
    Task RefreshAsync(CancellationToken cancellationToken = default);
}