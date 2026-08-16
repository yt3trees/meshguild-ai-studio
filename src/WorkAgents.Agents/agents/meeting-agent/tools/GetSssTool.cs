namespace WorkAgents.Agents.MeetingAgent.Tools;

/// <summary>外部サービスへ接続せず、SSS連携の入出力契約を示すサンプルツール。</summary>
public static class GetSssTool
{
    public static Task<GetSssResult> ExecuteAsync(
        string subject,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new GetSssResult(subject.Trim(), "sample"));
    }
}

public sealed record GetSssResult(string Subject, string Result);