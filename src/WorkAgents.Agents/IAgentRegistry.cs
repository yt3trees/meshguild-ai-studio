using WorkAgents.Agents.Loading;
using WorkAgents.Core.Abstractions;

namespace WorkAgents.Agents;

/// <summary>エージェント一覧(選択肢)の軽量ビュー。</summary>
public sealed record AgentView(
    string Name,
    string DisplayName,
    string Description,
    IReadOnlyList<SkillView> AttachedSkills,
    string SourceLabel = "standard");

/// <summary>エージェントにアタッチされた skill の表示用ビュー。</summary>
public sealed record SkillView(string Name, string Source, string Content);

/// <summary>エージェント設定から解決された利用可能 tool の読み取り専用ビュー。</summary>
public sealed record ToolView(
    string Name,
    string Description,
    string Source,
    string Approval,
    IReadOnlyList<string> Agents);

/// <summary>
/// エージェント名→実行の仲介(M1同期最小)。非同期ジョブ実行(M3)・HITL 承認(M4)は
/// Worker 側で別途実装するが、WebUI の同期チャットはここを通す。
/// </summary>
public interface IAgentRegistry
{
    IReadOnlyList<AgentView> ListAgents();

    IReadOnlyList<ToolView> ListTools();

    /// <summary>同期実行。応答テキストを返す(ストリーミングしない最小版)。</summary>
    Task<string> RunAsync(string agentName, string userMessage, CancellationToken cancellationToken = default);

    /// <summary>runの作業ディレクトリを指定して同期実行する(M3)。</summary>
    Task<string> RunAsync(
        string agentName,
        string userMessage,
        string workingDirectory,
        CancellationToken cancellationToken = default);

    /// <summary>承認ブリッジ付きのrun実行。承認要求は同じAgentSessionで再開する。</summary>
    Task<string> RunAsync(
        string agentName,
        string userMessage,
        string workingDirectory,
        string runId,
        CancellationToken cancellationToken = default);

    /// <summary>threadを復元して実行し、完了後に同じthreadへセッションを保存する。</summary>
    Task<string> RunAsync(
        string agentName,
        string userMessage,
        string? workingDirectory,
        string? threadId,
        string? runId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// ストリーミング実行。途中経過を列挙しながら 1 ターンを実行する。
    /// セッション復元・保存、コスト記録、HITL 承認の再開は一括版 (<see cref="RunAsync(string, string, string?, string?, string?, CancellationToken)"/>)
    /// と同等に行う。ストリームの最後には必ず <see cref="AgentCompletedUpdate"/> が 1 回だけ現れる。
    /// </summary>
    IAsyncEnumerable<AgentInvocationUpdate> RunStreamingAsync(
        string agentName,
        string userMessage,
        string? workingDirectory,
        string? threadId,
        string? runId,
        CancellationToken cancellationToken = default);
}