namespace WorkAgents.Core.Authoring;

/// <summary>診断の重大度。</summary>
public enum DiagnosticSeverity
{
    /// <summary>このままでは保存も実行もできない。</summary>
    Error,

    /// <summary>実行はできるが見直した方がよい。</summary>
    Warning,
}

/// <summary>
/// 定義の検証結果 1 件を、書き手が読める日本語に翻訳したもの
/// (案D「エラーメッセージの翻訳層」)。
/// <list type="bullet">
/// <item><see cref="Message"/>: 何がどこで起きているか。</item>
/// <item><see cref="Fix"/>: どう直せばよいか。</item>
/// <item><see cref="RawMessage"/>: 翻訳元の原文。ログや問い合わせ用に残す。</item>
/// </list>
/// </summary>
public sealed record AuthoringDiagnostic
{
    public required string Code { get; init; }

    public required string Message { get; init; }

    public string? Fix { get; init; }

    public DiagnosticSeverity Severity { get; init; } = DiagnosticSeverity.Error;

    /// <summary>問題のあるノード ID。GUI 上でハイライトするために使う。</summary>
    public IReadOnlyList<string> NodeIds { get; init; } = Array.Empty<string>();

    /// <summary>問題のあるエッジ ID。GUI 上でハイライトするために使う。</summary>
    public IReadOnlyList<string> EdgeIds { get; init; } = Array.Empty<string>();

    /// <summary>翻訳元のメッセージ。翻訳規則に当てはまらなかった場合はこれをそのまま見せる。</summary>
    public string? RawMessage { get; init; }

    /// <summary>1 行で表示するときの文言。</summary>
    public string ToDisplayLine()
        => string.IsNullOrWhiteSpace(Fix) ? Message : $"{Message} {Fix}";
}
