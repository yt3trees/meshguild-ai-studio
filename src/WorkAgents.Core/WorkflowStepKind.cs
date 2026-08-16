namespace WorkAgents.Core;

/// <summary>ワークフローステップの種別(5.13.1)。</summary>
public enum WorkflowStepKind
{
    /// <summary>既定。LLM エージェントを呼び出し、結果文字列を返す。</summary>
    Agent,

    /// <summary>
    /// C# スクリプトを実行し、戻り値(辞書 or 匿名型)を <c>${steps.&lt;name&gt;.output.&lt;key&gt;}</c> で参照可能にする。
    /// 実行はホストプロセス権限のため信頼できる作成者のみ配置可。
    /// </summary>
    Code,

    /// <summary>HITL 承認ゲート。WebUI /approvals に承認要求を表示し、決定されるまで待機。却下/タイムアウトでワークフロー全体を Abort。</summary>
    Approve,
}