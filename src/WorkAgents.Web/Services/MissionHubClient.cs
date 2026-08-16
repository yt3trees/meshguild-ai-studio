using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Configuration;
using System.Text;
using System.Text.Json;
using WorkAgents.Core.Abstractions;
using WorkAgents.Core.Missions;

namespace WorkAgents.Web.Services;

/// <summary>
/// <c>WorkAgents.Host</c> の <c>/hubs/missions</c> (T043、contracts/mission-hub.md) へ接続するクライアント (T046)。
/// 配信は最善努力であるため、接続確立時・再接続時には必ず <see cref="IMessageStore"/> を
/// 直接読んで <c>sinceSeq</c> からの差分を取り直す (Web は共有 SQLite を参照系として直接読む方針)。
/// </summary>
public sealed class MissionHubClient : IAsyncDisposable
{
    private readonly HubConnection _connection;
    private readonly IMessageStore _messageStore;
    private readonly Dictionary<string, long> _lastSeqByMission = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ActiveAgentStream> _streamsById = new(StringComparer.Ordinal);
    private readonly object _gate = new();

    /// <summary>あるミッションについて、接続時・再接続時の差分取得が完了した (呼び出し側は画面を更新する)。</summary>
    public event Func<string, IReadOnlyList<Message>, Task>? MessagesCaughtUp;

    /// <summary>新しい発言がハブから通知された (本文は含まない。呼び出し側は CatchUpAsync 相当で取りに行く)。</summary>
    public event Action<string>? MessageAppended;

    public event Action<string>? MissionStatusChanged;

    /// <summary>実行中エージェントの暫定発言が更新された (呼び出し側は <see cref="GetActiveStreams"/> を読み直す)。</summary>
    public event Action<string>? AgentStreamUpdated;

    public MissionHubClient(IConfiguration configuration, IMessageStore messageStore)
    {
        _messageStore = messageStore;
        var baseUrl = (configuration["Orchestration:HostBaseUrl"] ?? "http://localhost:5160").TrimEnd('/');

        _connection = new HubConnectionBuilder()
            .WithUrl($"{baseUrl}/hubs/missions")
            .WithAutomaticReconnect()
            .Build();

        _connection.On<Message>("MessageAppended", message =>
        {
            MessageAppended?.Invoke(message.MissionId);
            _ = CatchUpAsync(message.MissionId, CancellationToken.None);
        });
        _connection.On<AgentStreamStarted>("MessageStreamStarted", started =>
        {
            lock (_gate)
            {
                _streamsById[started.StreamId] = new ActiveAgentStream(
                    started.MissionId,
                    started.StreamId,
                    started.InstanceId,
                    started.AgentName);
            }

            AgentStreamUpdated?.Invoke(started.MissionId);
        });
        _connection.On<AgentStreamDelta>("MessageDelta", delta =>
        {
            lock (_gate)
            {
                if (!_streamsById.TryGetValue(delta.StreamId, out var stream))
                {
                    // Started を取り逃した場合は暫定表示を作らない。確定発言で追いつく。
                    return;
                }

                stream.Append(delta.SeqInStream, delta.TextDelta);
            }

            AgentStreamUpdated?.Invoke(delta.MissionId);
        });
        _connection.On<AgentStreamCompleted>("MessageStreamCompleted", completed =>
        {
            lock (_gate)
            {
                _streamsById.Remove(completed.StreamId);
            }

            AgentStreamUpdated?.Invoke(completed.MissionId);
        });
        _connection.On<JsonElement>("MissionStatusChanged", payload =>
        {
            var missionId = payload.TryGetProperty("missionId", out var value) ? value.GetString() : null;
            if (!string.IsNullOrWhiteSpace(missionId)) MissionStatusChanged?.Invoke(missionId);
        });

        _connection.Reconnected += async _ =>
        {
            string[] missionIds;
            lock (_gate)
            {
                // 切断中の増分は追えないため、暫定表示は捨てて確定発言で作り直す。
                _streamsById.Clear();
                missionIds = _lastSeqByMission.Keys.ToArray();
            }

            foreach (var missionId in missionIds)
            {
                await CatchUpAsync(missionId, CancellationToken.None);
            }
        };
    }

    public HubConnectionState State => _connection.State;

    public Task StartAsync(CancellationToken ct = default) => _connection.StartAsync(ct);

    /// <summary>ミッションを購読し、購読直後に <c>sinceSeq=0</c> からの差分 (=全件) を取り直す。</summary>
    public async Task SubscribeAsync(string missionId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(missionId);
        lock (_gate)
        {
            _lastSeqByMission.TryAdd(missionId, 0);
        }

        await _connection.InvokeAsync("Subscribe", missionId, ct);
        await CatchUpAsync(missionId, ct);
    }

    public Task UnsubscribeAsync(string missionId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(missionId);
        lock (_gate)
        {
            _lastSeqByMission.Remove(missionId);
        }

        return _connection.InvokeAsync("Unsubscribe", missionId, ct);
    }

    public Task SubscribeOverviewAsync(CancellationToken ct = default)
        => _connection.InvokeAsync("SubscribeOverview", ct);

    /// <summary>接続時・再接続時・MessageAppended 受信時の差分取得。</summary>
    private async Task CatchUpAsync(string missionId, CancellationToken ct)
    {
        long since;
        lock (_gate)
        {
            since = _lastSeqByMission.TryGetValue(missionId, out var s) ? s : 0;
        }

        var messages = await _messageStore.ListAsync(missionId, since, ct: ct);
        if (messages.Count > 0)
        {
            lock (_gate)
            {
                _lastSeqByMission[missionId] = messages[^1].Seq;
                DropSettledStreams(missionId, messages);
            }
        }

        if (MessagesCaughtUp is not null)
        {
            await MessagesCaughtUp.Invoke(missionId, messages);
        }
    }

    /// <summary>当該ミッションで進行中の暫定発言 (確定前) を返す。</summary>
    public IReadOnlyList<ActiveAgentStreamView> GetActiveStreams(string missionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(missionId);
        lock (_gate)
        {
            return _streamsById.Values
                .Where(stream => string.Equals(stream.MissionId, missionId, StringComparison.Ordinal))
                .Where(stream => stream.HasText)
                .Select(stream => stream.ToView())
                .ToArray();
        }
    }

    /// <summary>
    /// 確定発言が届いたインスタンスの暫定表示を落とす。
    /// <c>MessageStreamCompleted</c> より先に確定発言が届いた場合でも二重表示にならないようにする。
    /// </summary>
    private void DropSettledStreams(string missionId, IReadOnlyList<Message> messages)
    {
        var settled = messages
            .Where(message => message.SenderKind == MessageSenderKind.Agent)
            .Select(message => message.SenderInstanceId)
            .Where(instanceId => !string.IsNullOrEmpty(instanceId))
            .ToHashSet(StringComparer.Ordinal);
        if (settled.Count == 0)
        {
            return;
        }

        var stale = _streamsById
            .Where(pair => string.Equals(pair.Value.MissionId, missionId, StringComparison.Ordinal))
            .Where(pair => settled.Contains(pair.Value.InstanceId))
            .Select(pair => pair.Key)
            .ToArray();
        foreach (var streamId in stale)
        {
            _streamsById.Remove(streamId);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _connection.DisposeAsync();
    }

    /// <summary>受信中の暫定発言 1 件。欠落や順序逆転を検出したらそれ以降の増分を捨てる。</summary>
    private sealed class ActiveAgentStream
    {
        private readonly StringBuilder _text = new();
        private long _expectedSeq;
        private bool _broken;

        public ActiveAgentStream(string missionId, string streamId, string instanceId, string agentName)
        {
            MissionId = missionId;
            StreamId = streamId;
            InstanceId = instanceId;
            AgentName = agentName;
        }

        public string MissionId { get; }

        public string StreamId { get; }

        public string InstanceId { get; }

        public string AgentName { get; }

        public bool HasText => _text.Length > 0;

        public void Append(long seq, string text)
        {
            if (_broken)
            {
                return;
            }

            if (seq != _expectedSeq)
            {
                // 欠落・順序逆転を検出。中途半端な本文を見せるより、確定発言を待つ。
                _broken = true;
                return;
            }

            _expectedSeq++;
            _text.Append(text);
        }

        public ActiveAgentStreamView ToView()
            => new(StreamId, InstanceId, AgentName, _text.ToString());
    }
}

/// <summary>画面へ渡す暫定発言のスナップショット。</summary>
public sealed record ActiveAgentStreamView(
    string StreamId,
    string InstanceId,
    string AgentName,
    string Text);
