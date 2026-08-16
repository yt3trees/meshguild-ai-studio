namespace WorkAgents.Core;

/// <summary>
/// Web チャット(<c>/chat</c>)の1回の送信に対する実行メタデータ(Chat.razor の「Traces」タブ表示用)。
/// ツール呼び出し単位の詳細ではなく、run 単位(所要時間・使用モデル・成否)の軽量な記録に留める。
/// より詳細な監査ログ(ツール単位)は第14章の対象で別レイヤー(M8)。
/// </summary>
public sealed record ChatTraceEntry
{
    public required string ThreadId { get; init; }
    public required string AgentName { get; init; }
    public string? ModelName { get; init; }
    public string? Provider { get; init; }
    public required long DurationMs { get; init; }
    public bool Success { get; init; } = true;
    public string? ErrorMessage { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}
