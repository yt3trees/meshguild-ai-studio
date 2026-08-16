namespace WorkAgents.Core.Abstractions;

/// <summary>
/// ワークフロー code ステップの C# スクリプト実行抽象(5.13.1)。Host プロセス内で動く Roslyn が想定実装。
/// <paramref name="inputs"/> には他ステップの結果(<c>${steps.<name>.*}</c> を解決済みの値)と workflow.input が入る。
/// 戻り値は匿名型/辞書。プロパティ名が <c>${steps.&lt;name&gt;.output.&lt;key&gt;}</c> で参照できる。
/// </summary>
public interface IWorkflowScriptRunner
{
    Task<object?> RunAsync(string code, IReadOnlyDictionary<string, object?> inputs, CancellationToken ct = default);
}