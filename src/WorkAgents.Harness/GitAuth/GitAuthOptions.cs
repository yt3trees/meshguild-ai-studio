namespace WorkAgents.Harness.GitAuth;

/// <summary>
/// GitHub App 認証設定(5.5)。AppId/InstallationId は識別子(非秘密・appsettings 可)、
/// 秘密鍵(PEM)は <see cref="ISecretStore"/> 経由で取得する(命名は <see cref="PrivateKeySecretName"/>)。
/// </summary>
public sealed class GitAuthOptions
{
    /// <summary>GitHub App の Integration Id(数値)。appsettings の <c>GitAuth:AppId</c>。</summary>
    public int AppId { get; set; }

    /// <summary>インストール先の Installation Id(数号)。appsettings の <c>GitAuth:InstallationId</c>。</summary>
    public long InstallationId { get; set; }

    /// <summary>秘密鍵を格納した <see cref="ISecretStore"/> 上の名前。</summary>
    public string PrivateKeySecretName { get; set; } = "github-app-private-key";

    /// <summary>User-Agent(Octokit の ProductHeaderValue)。既定 <c>work-agents</c>。</summary>
    public string UserAgent { get; set; } = "work-agents";

    /// <summary>App JWT の有効期限(秒)。GitHub 推奨は最大 600。既定 540(9 分)。</summary>
    public int JwtExpirationSeconds { get; set; } = 540;

    /// <summary>
    /// git-credentials の書き込み先。未設定時は <c>{HOME|USERPROFILE}/.git-credentials</c>。
    /// 明示設定すれば非自明な経路(<c>%LOCALAPPDATA%\work-agents\git\.git-credentials</c> 等)にも置ける。
    /// </summary>
    public string? CredentialFilePath { get; set; }

    /// <summary>true: 初期化時に <c>git config --global credential.helper store</c> を実行しない(呼び出し側で管理)。</summary>
    public bool SkipGitConfig { get; set; }
}