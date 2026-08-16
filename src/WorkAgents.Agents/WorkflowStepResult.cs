using System.Collections.Generic;

namespace WorkAgents.Agents;

/// <summary>ワークフローステップの実行結果(5.13.1)。テンプレート参照と code ステップ Inputs 構築に使う。</summary>
internal sealed class WorkflowStepResult
{
    /// <summary>テキスト表現結果。agent: 応答テキスト。code: JSON 化戻り値。approve: "approved"。</summary>
    public string? Result { get; init; }

    /// <summary>code ステップの戻り値をフラット化した辞書(匿名型プロパティ → string,object?)。agent/approve は空。</summary>
    public IReadOnlyDictionary<string, object?> Output { get; init; } = new Dictionary<string, object?>();

    /// <summary>code ステップの生戻り値。スクリプト Inputs にそのまま渡すために保持。</summary>
    public object? Raw { get; init; }

    public static WorkflowStepResult FromString(string result)
        => new() { Result = result, Output = new Dictionary<string, object?>() };
}