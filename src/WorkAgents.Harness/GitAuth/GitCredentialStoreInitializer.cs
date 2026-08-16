using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace WorkAgents.Harness.GitAuth;

/// <summary>
/// git-credentials ファイル + <c>git config --global credential.helper store</c> の設定(5.5)。
/// <see cref="IGitAuth"/> 実装から呼ばれ、installation token をファイルに書き込む。
/// トークンをコマンド文字列/ログ/例外に漏らさない(ファイル 0600 のみ)。
/// </summary>
public sealed class GitCredentialStoreInitializer
{
    private readonly string _credentialFilePath;
    private readonly bool _skipGitConfig;
    private readonly ILogger<GitCredentialStoreInitializer>? _logger;

    public GitCredentialStoreInitializer(GitAuthOptions options, ILogger<GitCredentialStoreInitializer>? logger = null)
    {
        _skipGitConfig = options.SkipGitConfig;
        _credentialFilePath = ResolveCredentialPath(options.CredentialFilePath);
        _logger = logger;
    }

    /// <summary>書き込み先ファイルパス(テスト/ログ用)。ファイル内容に token は含まれない前提で公開。</summary>
    public string CredentialFilePath => _credentialFilePath;

    /// <summary>
    /// installation token を git-credentials ファイルに上書き書き込みし、ファイル権限を 0600
    /// (Windows は現在ユーザーのみアクセス許可)にする。git config 設定は初期化時に1回だけ実行。
    /// </summary>
    public async Task WriteTokenAsync(InstallationToken token, CancellationToken cancellationToken = default)
    {
        var dir = Path.GetDirectoryName(_credentialFilePath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        // 既定 URL は github.com。別ホスト(GHES)は将来 options で拡張する。
        var line = $"https://x-access-token:{token.Token}@github.com\n";
        await File.WriteAllTextAsync(_credentialFilePath, line, cancellationToken);

        ApplyFilePermissions(_credentialFilePath);

        if (!_skipGitConfig)
        {
            await EnsureGitConfigAsync(cancellationToken);
        }
    }

    private async Task EnsureGitConfigAsync(CancellationToken cancellationToken)
    {
        // git が未導入でもエージェントpragmaビリティを保つため、失敗は警告に留める。
        try
        {
            var psi = new ProcessStartInfo("git", "config --global credential.helper store")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            using var p = Process.Start(psi);
            if (p is null)
            {
                _logger?.LogWarning("git executable not found; credential.helper store skipped.");
                return;
            }
            await p.WaitForExitAsync(cancellationToken);
            if (p.ExitCode != 0)
            {
                _logger?.LogWarning("git config credential.helper store exited {Code}: {Stderr}",
                    p.ExitCode, await p.StandardError.ReadToEndAsync(cancellationToken));
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "git config credential.helper store failed (ignored).");
        }
    }

    private static string ResolveCredentialPath(string? explicitPath)
    {
        if (!string.IsNullOrWhiteSpace(explicitPath))
        {
            return Path.GetFullPath(explicitPath);
        }

        var home = Environment.GetEnvironmentVariable("HOME");
        if (string.IsNullOrWhiteSpace(home))
        {
            home = Environment.GetEnvironmentVariable("USERPROFILE");
        }
        if (string.IsNullOrWhiteSpace(home))
        {
            home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }
        return Path.GetFullPath(Path.Combine(home, ".git-credentials"));
    }

    private void ApplyFilePermissions(string path)
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS()) return;
        try
        {
            // chmod 600
            var psi = new ProcessStartInfo("chmod", $"600 \"{path}\"")
            {
                UseShellExecute = false,
                RedirectStandardError = true,
            };
            using var p = Process.Start(psi);
            p?.WaitForExit();
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "chmod 600 failed on {Path}", path);
        }
    }
}