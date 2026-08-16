using GitHubJwt;
using Microsoft.Extensions.Logging;
using Octokit;
using WorkAgents.Core.Abstractions;

namespace WorkAgents.Harness.GitAuth;

/// <summary>
/// <see cref="IInstallationTokenSource"/> の GitHub App 実装(5.5)。
/// <list type="bullet">
/// <item>秘密鍵は <see cref="ISecretStore"/> 経由で取得し、メモリにも Volt で残さない(都度 Pam のみ読み取り)。</item>
/// <item>JWT は <see cref="GitHubJwtFactory"/>(RS256)で署名。有効期限は <see cref="GitAuthOptions.JwtExpirationSeconds"/>。</item>
/// <item>installation token の発行は Octokit の <c>GitHubApps.CreateInstallationToken(installationId)</c>。</item>
/// <item>トークン文字列をログ/例外メッセージ/シリアライズに出さない(鉄則)。</item>
/// </list>
/// </summary>
public sealed class GitHubAppTokenMinter : IInstallationTokenSource
{
    private readonly GitAuthOptions _options;
    private readonly ISecretStore _secrets;
    private readonly ILogger<GitHubAppTokenMinter>? _logger;

    public GitHubAppTokenMinter(GitAuthOptions options, ISecretStore secrets, ILogger<GitHubAppTokenMinter>? logger = null)
    {
        _options = options;
        _secrets = secrets;
        _logger = logger;
    }

    public async Task<InstallationToken> MintAsync(CancellationToken cancellationToken = default)
    {
        if (_options.AppId <= 0)
        {
            throw new InvalidOperationException("GitAuth:AppId is required.");
        }
        if (_options.InstallationId <= 0)
        {
            throw new InvalidOperationException("GitAuth:InstallationId is required.");
        }

        var pem = await _secrets.GetAsync(_options.PrivateKeySecretName, cancellationToken);
        if (string.IsNullOrWhiteSpace(pem))
        {
            throw new InvalidOperationException(
                $"GitHub App private key not found in secret store (name='{_options.PrivateKeySecretName}').");
        }

        string jwt;
        var src = new StringPrivateKeySource(pem!);
        var factory = new GitHubJwtFactory(src, new GitHubJwtFactoryOptions
        {
            AppIntegrationId = _options.AppId,
            ExpirationSeconds = _options.JwtExpirationSeconds,
        });
        jwt = factory.CreateEncodedJwtToken();

        var appClient = new GitHubClient(new ProductHeaderValue(_options.UserAgent))
        {
            Credentials = new Credentials(jwt, AuthenticationType.Bearer),
        };

        AccessToken token;
        try
        {
            token = await appClient.GitHubApps.CreateInstallationToken(_options.InstallationId);
        }
        catch (Exception ex)
        {
            // 例外メッセージに token/jwt を含めない(JWT を入れた変数名は出さない)。
            _logger?.LogError("GitHub installation token request failed (no token in logs).");
            throw new InvalidOperationException("GitHub installation token request failed.", ex);
        }

        _logger?.LogInformation("minted GitHub App installation token (expires {ExpiresAt:O}).", token.ExpiresAt);
        return new InstallationToken(token.Token, token.ExpiresAt);
    }
}