using System.Text;

namespace WorkAgents.Core.Abstractions;

/// <summary>
/// 1 ストリーム分の増分をまとめる先入れ先出しのバッファ。
/// 受信側 (Blazor Server) の再描画コストを抑えるため、<see cref="IAgentStreamSink"/> の実装が
/// 増分をここへ通してから送出する。送出単位で通し番号を採番し直すので、
/// 受信側は連番の欠落だけを見れば取りこぼしを検出できる。
/// </summary>
public sealed class AgentStreamDeltaBuffer
{
    private readonly object _gate = new();
    private readonly StringBuilder _pending = new();
    private readonly TimeSpan _interval;
    private readonly int _threshold;
    private DateTimeOffset _lastFlush;
    private long _nextSeq;

    public AgentStreamDeltaBuffer(DateTimeOffset now, TimeSpan interval, int threshold)
    {
        _lastFlush = now;
        _interval = interval;
        _threshold = threshold;
    }

    /// <summary>
    /// 増分を追加し、送出すべきならその内容を返す。
    /// 閾値の文字数に達したか、前回送出から <c>interval</c> が経過したときに送出する。
    /// </summary>
    public (long Seq, string Text)? Append(string text, DateTimeOffset now)
    {
        lock (_gate)
        {
            _pending.Append(text);
            if (_pending.Length < _threshold && now - _lastFlush < _interval)
            {
                return null;
            }

            _lastFlush = now;
            return Take();
        }
    }

    /// <summary>残っている増分をすべて取り出す。ストリーム終了時に呼ぶ。</summary>
    public (long Seq, string Text)? Drain()
    {
        lock (_gate)
        {
            return _pending.Length == 0 ? null : Take();
        }
    }

    private (long Seq, string Text) Take()
    {
        var text = _pending.ToString();
        _pending.Clear();
        return (_nextSeq++, text);
    }
}
