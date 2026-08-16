using Octokit;

namespace WorkAgents.Harness.GitAuth;

/// <summary>
/// GitHub App JWT を署名し installation token を発行する(5.5)。<see cref="ISecretStore"/> から秘密鍵を取得し、
/// GitHubJwt で JWT を作り、Octokit の <c>GitHubApps.CreateInstallationToken</c> を呼ぶ。
/// トークン文字列は本インターフェースの呼び出し元(git-credentials 書き込み層)のみに渡し、LLM/コマンド文字列に露出しない。
/// </summary>
public interface IInstallationTokenSource
{
    Task<InstallationToken> MintAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// 取得した installation token。token 文字列は破棄されるまで呼び出し元のみが触る。
/// </summary>
public sealed record InstallationToken(string Token, DateTimeOffset ExpiresAt);