using WorkAgents.Core.Abstractions;

namespace WorkAgents.Infrastructure.Stores;

/// <summary>成果物ダウンロード要求の解決結果(FR-008〜FR-012)。</summary>
public abstract record ArtifactDownloadResult
{
    private ArtifactDownloadResult()
    {
    }

    public sealed record Found(Stream Content, string FileName, string ContentType) : ArtifactDownloadResult;

    /// <summary>未検出・破棄済み・ファイル本体が見つからない場合をすべて含む(存在有無の詳細を外部へ漏らさない)。</summary>
    public sealed record NotFound : ArtifactDownloadResult;
}

/// <summary>
/// <see cref="IMissionArtifactStore"/> を使って、指定ミッション配下の成果物IDからダウンロード可能な
/// ストリームを解決する。破棄済み成果物や、ミッションに属さない成果物IDは一律で<see cref="ArtifactDownloadResult.NotFound"/>
/// として扱い、存在有無の情報を外部へ漏らさない(<c>ListMissionAsync</c>がmissionId単位でフィルタするため、
/// 他ミッションの成果物IDを指定してもそもそも一覧に現れない)。
/// </summary>
public sealed class ArtifactDownloadResolver
{
    private static readonly IReadOnlyDictionary<string, string> ContentTypesByExtension = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        [".txt"] = "text/plain",
        [".md"] = "text/markdown",
        [".json"] = "application/json",
        [".csv"] = "text/csv",
        [".html"] = "text/html",
        [".pdf"] = "application/pdf",
        [".png"] = "image/png",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".zip"] = "application/zip",
    };

    private readonly IMissionArtifactStore _artifacts;

    public ArtifactDownloadResolver(IMissionArtifactStore artifacts)
    {
        _artifacts = artifacts;
    }

    public async Task<ArtifactDownloadResult> ResolveAsync(string missionId, string artifactId, CancellationToken ct = default)
    {
        var list = await _artifacts.ListMissionAsync(missionId, includeDiscarded: true, ct);
        var artifact = list.FirstOrDefault(a => string.Equals(a.ArtifactId, artifactId, StringComparison.Ordinal));
        if (artifact is null || artifact.DiscardedAt is not null)
        {
            return new ArtifactDownloadResult.NotFound();
        }

        var stream = await _artifacts.OpenReadAsync(artifact.Path, ct);
        if (stream is null)
        {
            return new ArtifactDownloadResult.NotFound();
        }

        var fileName = Path.GetFileName(artifact.Path);
        var contentType = ContentTypesByExtension.TryGetValue(Path.GetExtension(fileName), out var mapped)
            ? mapped
            : "application/octet-stream";
        return new ArtifactDownloadResult.Found(stream, fileName, contentType);
    }
}
