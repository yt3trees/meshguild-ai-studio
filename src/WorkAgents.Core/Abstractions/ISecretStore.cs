namespace WorkAgents.Core.Abstractions;

/// <summary>
/// 秘密(トークン・鍵・接続文字列)の安全保管(5.5, 5.11)。
/// Local: .NET user-secrets / DPAPI。Azure: Key Vault + マネージドID。
/// トークンを LLM/コマンド文字列/ログに出さない(鉄則:5.5)。
/// </summary>
public interface ISecretStore
{
    Task<string?> GetAsync(string name, CancellationToken ct = default);

    Task SetAsync(string name, string value, CancellationToken ct = default);

    /// <summary>保存済みの秘密の名前一覧(値は含まない)。管理UI/APIでの棚卸し・削除に使う。</summary>
    Task<IReadOnlyList<string>> ListAsync(CancellationToken ct = default);

    /// <summary>指定した秘密を削除する。存在しなければ false。</summary>
    Task<bool> DeleteAsync(string name, CancellationToken ct = default);
}