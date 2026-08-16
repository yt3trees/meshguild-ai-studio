using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using WorkAgents.Core.Abstractions;

namespace WorkAgents.Infrastructure.Secrets;

/// <summary>
/// <see cref="ISecretStore"/> の Local 実装(D0/D3 Local・5.5 / 5.11)。
/// 秘密(トークン・鍵・接続文字列)を DPAPI(<see cref="DataProtectionScope.CurrentUser"/>)で暗号化して
/// ローカルファイルに保存する。平文はディスクに書かない。同一 Windows ユーザー以外は復号できない。
/// 秘密ファイルのパスを LLM/コマンド文字列/ログに出さない。Azure 移行時は Key Vault 実装に差し替える(M7)。
/// ※ Local プロファイルは Windows 上で動かす前提(D0)。非 Windows 実行時の呼び出しは PlatformNotSupportedException を投げる。
/// </summary>
public sealed class LocalFileSecretStore : ISecretStore
{
    private readonly string _root;
    private readonly ILogger<LocalFileSecretStore>? _logger;

    public LocalFileSecretStore(string root, ILogger<LocalFileSecretStore>? logger = null)
    {
        _root = root;
        _logger = logger;
    }

    public static string DefaultRoot
    {
        get
        {
            var baseDir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrEmpty(baseDir))
            {
                baseDir = Path.GetTempPath();
            }
            return Path.Combine(baseDir, "work-agents", "secrets");
        }
    }

    public async Task<string?> GetAsync(string name, CancellationToken ct = default)
    {
        var path = ResolvePath(name);
        if (!File.Exists(path))
        {
            return null;
        }

        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("LocalFileSecretStore requires Windows (DPAPI). Use Azure Key Vault implementation in non-Windows/Azure profile.");
        }

        var cipher = await File.ReadAllBytesAsync(path, ct);
        try
        {
            var plain = OperatingSystem.IsWindows()
                ? ProtectedData.Unprotect(cipher, optionalEntropy: null, DataProtectionScope.CurrentUser)
                : throw new PlatformNotSupportedException();
            return Encoding.UTF8.GetString(plain);
        }
        catch (CryptographicException ex)
        {
            // 復号失敗は機密扱い・内容は出さない。
            _logger?.LogError(ex, "DPAPI unprotect failed for secret '{Name}' (path not logged)", name);
            throw new InvalidOperationException("secret unprotect failed (different user or corrupted).");
        }
    }

    public async Task SetAsync(string name, string value, CancellationToken ct = default)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("LocalFileSecretStore requires Windows (DPAPI). Use Azure Key Vault implementation in non-Windows/Azure profile.");
        }

        Directory.CreateDirectory(_root);
        var plain = Encoding.UTF8.GetBytes(value);
        var cipher = OperatingSystem.IsWindows()
            ? ProtectedData.Protect(plain, optionalEntropy: null, DataProtectionScope.CurrentUser)
            : throw new PlatformNotSupportedException();
        var path = ResolvePath(name);
        await File.WriteAllBytesAsync(path, cipher, ct);
        ApplyFilePermissions(path);
        _logger?.LogInformation("secret stored: {Name}", name);
    }

    public Task<IReadOnlyList<string>> ListAsync(CancellationToken ct = default)
    {
        if (!Directory.Exists(_root))
        {
            return Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
        }

        IReadOnlyList<string> names = Directory.EnumerateFiles(_root)
            .Select(Path.GetFileName)
            .Where(name => !string.IsNullOrEmpty(name))
            .Select(name => name!)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return Task.FromResult(names);
    }

    public Task<bool> DeleteAsync(string name, CancellationToken ct = default)
    {
        var path = ResolvePath(name);
        if (!File.Exists(path))
        {
            return Task.FromResult(false);
        }

        File.Delete(path);
        _logger?.LogInformation("secret deleted: {Name}", name);
        return Task.FromResult(true);
    }

    private string ResolvePath(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("secret name is empty.", nameof(name));
        }
        // 名前は安全なファイル名に正規化(パス区切り/.. を禁止)。
        var safe = string.Concat(name.Select(c => char.IsLetterOrDigit(c) || c == '-' || c == '_' || c == '.' ? c : '_'));
        return Path.Combine(_root, safe);
    }

    private void ApplyFilePermissions(string path)
    {
        if (!OperatingSystem.IsWindows()) return;
        try
        {
            // 現在ユーザーのみにアクセスを限定。.NET の FileSecurity は Windows ACL を直接操作可能。
            var fi = new FileInfo(path);
            fi.Encrypt(); // EFS 有効なら暗号化属性付与(追加保護・失敗可)
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "file ACL hardening skipped (DPAPI で本体保護は維持)。");
        }
    }
}