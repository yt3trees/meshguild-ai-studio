namespace WorkAgents.Core.Abstractions;

/// <summary>
/// 非同期 run を投入・受領するキュー(5.6)。
/// Local: プロセス内 <c>Channel&lt;T&gt;</c>。Azure: Storage Queue / Service Bus。
/// 実装は <c>WorkAgents.Infrastructure</c> で差し替え。
/// </summary>
public interface IRunQueue
{
    ValueTask EnqueueAsync(string runId, CancellationToken ct = default);

    IAsyncEnumerable<string> ReadAllAsync(CancellationToken ct = default);
}