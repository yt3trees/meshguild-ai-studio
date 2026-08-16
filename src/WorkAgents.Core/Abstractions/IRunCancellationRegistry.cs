namespace WorkAgents.Core.Abstractions;

/// <summary>
/// 実行中のRunに対する協調的キャンセルを仲介する(5.6)。
/// <see cref="Register"/> でRun専用の <see cref="CancellationTokenSource"/> を登録し、
/// <see cref="TryCancel"/> でHost APIからのキャンセル要求をそのRunの実行に伝える。
/// Local/Azureいずれのプロファイルでもプロセス内で完結するため実装は共通。
/// </summary>
public interface IRunCancellationRegistry
{
    /// <summary>指定Runの実行用に、<paramref name="linkedToken"/>(host shutdown等)と連動する新しいトークンソースを登録する。</summary>
    CancellationTokenSource Register(string runId, CancellationToken linkedToken);

    /// <summary>登録済みのRunがあればキャンセルを要求する。登録が無ければ false。</summary>
    bool TryCancel(string runId);

    /// <summary>
    /// <see cref="TryCancel"/> による明示キャンセルだったかを返す。run timeout の <c>CancelAfter</c> による
    /// キャンセルとの区別に使う(呼び出し側が完了メッセージを出し分けるため)。<see cref="Remove"/> 後は false。
    /// </summary>
    bool WasExplicitlyCancelled(string runId);

    /// <summary>Run完了後に登録を解除する。</summary>
    void Remove(string runId);
}
