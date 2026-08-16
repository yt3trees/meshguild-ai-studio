using Microsoft.AspNetCore.SignalR;

namespace WorkAgents.Host;

/// <summary>
/// ミッション向け SignalR ハブ (T043、contracts/mission-hub.md)。
/// 既存の <see cref="RunProgressHub"/> (<c>/hubs/runs</c>) は変更せず、別ハブとして追加する。
/// 認証は無くループバック運用を前提とする (憲法 III)。配信は最善努力であり、
/// クライアントは接続確立時・再接続時に <c>GET /missions/{id}/messages?sinceSeq=</c> で差分を取り直す。
/// </summary>
public sealed class MissionHub : Hub
{
    private const string OverviewGroup = "missions:overview";

    public static string GroupName(string missionId) => $"mission:{missionId}";

    /// <summary>当該ミッションのグループへ参加する。</summary>
    public Task Subscribe(string missionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(missionId);
        return Groups.AddToGroupAsync(Context.ConnectionId, GroupName(missionId));
    }

    /// <summary>ミッションのグループから離脱する。</summary>
    public Task Unsubscribe(string missionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(missionId);
        return Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupName(missionId));
    }

    /// <summary>Mission Control 用。全ミッションのサマリー更新を受け取る。</summary>
    public Task SubscribeOverview()
        => Groups.AddToGroupAsync(Context.ConnectionId, OverviewGroup);
}
