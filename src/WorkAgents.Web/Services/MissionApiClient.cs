using System.Net.Http.Json;
using Microsoft.Extensions.Configuration;

namespace WorkAgents.Web.Services;

public sealed class MissionApiClient
{
    private readonly HttpClient _http;

    public MissionApiClient(IConfiguration configuration)
    {
        var baseUrl = (configuration["Orchestration:HostBaseUrl"] ?? "http://localhost:5160").TrimEnd('/') + "/";
        _http = new HttpClient { BaseAddress = new Uri(baseUrl) };
    }

    /// <summary>HostのベースURL。成果物ダウンロードリンクなど、直接リンクを組み立てる画面向けに公開する。</summary>
    public Uri BaseAddress => _http.BaseAddress!;

    /// <summary>成果物ダウンロードURL(<c>GET /missions/{missionId}/artifacts/{artifactId}/content</c>、004-workspace-artifact-lifecycle FR-009)。</summary>
    public string BuildArtifactDownloadUrl(string missionId, string artifactId)
        => new Uri(BaseAddress, $"missions/{Uri.EscapeDataString(missionId)}/artifacts/{Uri.EscapeDataString(artifactId)}/content").ToString();

    public async Task SendInstructionAsync(string missionId, string body, string? targetInstanceId = null, CancellationToken ct = default)
    {
        using var response = await _http.PostAsJsonAsync($"missions/{Uri.EscapeDataString(missionId)}/messages", new { body, targetInstanceId }, ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task<string> SubmitAsync(
        string goal,
        string targetKind,
        string targetName,
        double? costLimitUsd = null,
        int? timeLimitSeconds = null,
        int? maxIterations = null,
        int? maxConcurrentAgents = null,
        CancellationToken ct = default)
    {
        using var response = await _http.PostAsJsonAsync(
            "missions",
            new
            {
                goal,
                targetKind,
                targetName,
                budget = new { costLimitUsd, timeLimitSeconds, maxIterations, maxConcurrentAgents },
            },
            ct);
        response.EnsureSuccessStatusCode();
        var accepted = await response.Content.ReadFromJsonAsync<MissionAcceptedResponse>(cancellationToken: ct)
            ?? throw new InvalidOperationException("Mission submission returned an empty response.");
        return accepted.MissionId;
    }

    private sealed record MissionAcceptedResponse(string MissionId, string Status, string? QueuedReason, int? QueuePosition);
}
