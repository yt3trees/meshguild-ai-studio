using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using WorkAgents.Core.Abstractions;
using WorkAgents.Infrastructure.Stores;

namespace WorkAgents.Host.Mcp;

[McpServerResourceType]
public sealed class McpResourceCatalog
{
    private readonly IMissionStore _missions;
    private readonly IMessageStore _messages;
    private readonly IMissionArtifactStore _artifacts;
    private readonly ArtifactDownloadResolver _downloads;
    private readonly ISecretRedactor _redactor;
    private readonly McpObservationTools _observations;
    private readonly McpResourceAccessPolicy _policy;

    public McpResourceCatalog(
        IMissionStore missions,
        IMessageStore messages,
        IMissionArtifactStore artifacts,
        ArtifactDownloadResolver downloads,
        ISecretRedactor redactor,
        McpObservationTools observations,
        McpResourceAccessPolicy policy)
    {
        _missions = missions;
        _messages = messages;
        _artifacts = artifacts;
        _downloads = downloads;
        _redactor = redactor;
        _observations = observations;
        _policy = policy;
    }

    [McpServerResource(
        UriTemplate = "workagents://missions/{missionId}",
        Name = "workagents_mission",
        Title = "WorkAgents Mission",
        MimeType = "application/json"),
        Description("Read a safe Mission snapshot.")]
    public async Task<string> workagents_mission_resource(string missionId, CancellationToken cancellationToken = default)
    {
        var mission = await GetMissionAsync(missionId, cancellationToken);
        return JsonSerializer.Serialize(McpMissionTools.ToSnapshot(mission));
    }

    [McpServerResource(
        UriTemplate = "workagents://missions/{missionId}/graph",
        Name = "workagents_graph",
        Title = "WorkAgents Graph Observation",
        MimeType = "application/json"),
        Description("Read bounded Graph and Loop execution state.")]
    public async Task<string> workagents_graph_resource(string missionId, CancellationToken cancellationToken = default)
    {
        await GetMissionAsync(missionId, cancellationToken);
        var observation = await _observations.BuildAsync(missionId, 0, cancellationToken);
        return JsonSerializer.Serialize(observation);
    }

    [McpServerResource(
        UriTemplate = "workagents://missions/{missionId}/messages",
        Name = "workagents_messages",
        Title = "WorkAgents Mission Messages",
        MimeType = "application/json"),
        Description("Read bounded, redacted Mission message summaries.")]
    public async Task<string> workagents_messages_resource(string missionId, CancellationToken cancellationToken = default)
    {
        await GetMissionAsync(missionId, cancellationToken);
        var messages = await _messages.ListAsync(missionId, limit: 100, ct: cancellationToken);
        var result = new List<object>(messages.Count);
        foreach (var message in messages)
        {
            result.Add(new
            {
                messageId = message.MessageId,
                seq = message.Seq,
                senderKind = message.SenderKind.ToString(),
                kind = message.Kind.ToString(),
                body = McpResponseProjector.SafeText(await _redactor.RedactAsync(message.Body, cancellationToken), 4000),
                createdAt = message.CreatedAt,
            });
        }

        return JsonSerializer.Serialize(result);
    }

    [McpServerResource(
        UriTemplate = "workagents://missions/{missionId}/artifacts/{artifactId}",
        Name = "workagents_artifact",
        Title = "WorkAgents Artifact",
        MimeType = "text/plain"),
        Description("Read a bounded text Artifact owned by a Mission.")]
    public async Task<string> workagents_artifact_resource(
        string missionId,
        string artifactId,
        CancellationToken cancellationToken = default)
    {
        await GetMissionAsync(missionId, cancellationToken);
        if (!McpResourceAccessPolicy.IsSafeIdentifier(artifactId))
        {
            throw new McpException("[artifact_unavailable] Artifact identifier is invalid.");
        }

        var artifact = (await _artifacts.ListMissionAsync(missionId, includeDiscarded: true, ct: cancellationToken))
            .FirstOrDefault(item => string.Equals(item.ArtifactId, artifactId, StringComparison.Ordinal));
        if (artifact is null || artifact.DiscardedAt is not null)
        {
            throw new McpException("[artifact_unavailable] Artifact is not available.");
        }

        var download = await _downloads.ResolveAsync(missionId, artifactId, cancellationToken);
        if (download is not ArtifactDownloadResult.Found found
            || !McpResourceAccessPolicy.IsTextContentType(found.ContentType))
        {
            if (download is ArtifactDownloadResult.Found foundContent)
            {
                await foundContent.Content.DisposeAsync();
            }
            throw new McpException("[artifact_unavailable] Artifact content is unavailable or not a supported text type.");
        }

        await using var stream = found.Content;
        return await _policy.ReadTextAsync(stream, cancellationToken);
    }

    private async Task<WorkAgents.Core.Missions.Mission> GetMissionAsync(string missionId, CancellationToken ct)
    {
        if (!McpResourceAccessPolicy.IsSafeIdentifier(missionId))
        {
            throw new McpException("[mission_not_found] Mission was not found.");
        }

        return await _missions.GetAsync(missionId, ct)
            ?? throw new McpException("[mission_not_found] Mission was not found.");
    }
}
