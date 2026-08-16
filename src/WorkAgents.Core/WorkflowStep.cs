namespace WorkAgents.Core;

/// <summary>
/// ワークフローの1ステップ(5.13.1)。各ステップは <see cref="Kind"/> に応じて
/// LLM エージェント(Agent)・C# スクリプト(Code)・HITL 承認(Approve)のいずれかを起動する。
/// </summary>
public sealed record WorkflowStep
{
    public required string Name { get; init; }

    public WorkflowStepKind Kind { get; init; } = WorkflowStepKind.Agent;

    /// <summary>Kind=Agent の場合の呼び出し先エージェント名。</summary>
    public string? Agent { get; init; }

    /// <summary>Kind=Agent の場合の入力テンプレート。${workflow.input} と ${steps.<name>.*} を置換。</summary>
    public string? Input { get; init; }

    /// <summary>Kind=Code の場合の C# スクリプト本文。Inputs IDictionary<string,object?> を参照可。</summary>
    public string? Code { get; init; }

    /// <summary>
    /// Kind=Code の場合に <see cref="Code"/> の代わりに参照する C# スクリプトファイルの絶対パス。
    /// <c>workflow.yaml</c> の <c>codeFile:</c>(workflow フォルダからの相対)を loader が解決して格納。
    /// インライン <see cref="Code"/> より優先される。.cs と .csx 両対応(Roslyn scripting は拡張子非依存・両方とも C# Script 構文)。
    /// </summary>
    public string? CodeFile { get; init; }

    /// <summary>Kind=Approve の承認要求のタイトル。未設定時は Name を使用。Web /approvals とデスクトップ通知に表示。</summary>
    public string? Title { get; init; }

    /// <summary>Kind=Approve の承認要求の要約(コマンド/差分プレビュー相当)テンプレート。</summary>
    public string? Summary { get; init; }

    /// <summary>Kind=Approve の承認タイムアウト。未設定時は既定(15分)。</summary>
    public TimeSpan? Timeout { get; init; }
}