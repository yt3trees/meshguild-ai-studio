namespace WorkAgents.Core.Abstractions;

/// <summary>
/// 秘匿処理の境界 (T023)。登録済みの秘密情報 (ISecretStore) の値、
/// および URL エンコード形・Base64 エンコード形を検出して伏せ字化する。
/// 永続化直前 (messages.body、artifacts.summary、evaluations.notes、
/// missions.error、approvals.args_summary 等) に必ず通す単一の経路とする (憲法 I、FR-012)。
/// </summary>
public interface ISecretRedactor
{
    /// <summary>入力文字列中の秘密情報を伏せ字化した文字列を返す。秘密情報が無ければそのまま返す。</summary>
    Task<string> RedactAsync(string text, CancellationToken ct = default);
}
