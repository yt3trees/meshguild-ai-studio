using System.Text;
using WorkAgents.Core.Missions;
using WorkAgents.Infrastructure.Stores;

namespace WorkAgents.UnitTests.Stores;

public sealed class ArtifactDownloadResolverTests
{
    [Fact]
    public async Task ResolveAsync_ReturnsFound_ForExistingNonDiscardedArtifact()
    {
        var (databasePath, artifactsRoot) = CreatePaths();
        try
        {
            var store = new SqliteArtifactStore(databasePath, artifactsRoot);
            var path = await store.SaveAsync("mission-1", "report.txt", ToStream("hello world"));
            var artifact = CreateArtifact("artifact-1", "mission-1", path, discardedAt: null);
            await store.SaveMissionArtifactAsync(artifact);
            var resolver = new ArtifactDownloadResolver(store);

            var result = await resolver.ResolveAsync("mission-1", "artifact-1");

            var found = Assert.IsType<ArtifactDownloadResult.Found>(result);
            Assert.Equal("report.txt", found.FileName);
            Assert.Equal("text/plain", found.ContentType);
            using var reader = new StreamReader(found.Content);
            Assert.Equal("hello world", await reader.ReadToEndAsync());
        }
        finally
        {
            CleanUp(databasePath, artifactsRoot);
        }
    }

    [Fact]
    public async Task ResolveAsync_ReturnsNotFound_ForDiscardedArtifact()
    {
        var (databasePath, artifactsRoot) = CreatePaths();
        try
        {
            var store = new SqliteArtifactStore(databasePath, artifactsRoot);
            var path = await store.SaveAsync("mission-1", "report.txt", ToStream("hello world"));
            var artifact = CreateArtifact("artifact-1", "mission-1", path, discardedAt: DateTimeOffset.UtcNow);
            await store.SaveMissionArtifactAsync(artifact);
            var resolver = new ArtifactDownloadResolver(store);

            var result = await resolver.ResolveAsync("mission-1", "artifact-1");

            Assert.IsType<ArtifactDownloadResult.NotFound>(result);
        }
        finally
        {
            CleanUp(databasePath, artifactsRoot);
        }
    }

    [Fact]
    public async Task ResolveAsync_ReturnsNotFound_ForUnknownArtifactId()
    {
        var (databasePath, artifactsRoot) = CreatePaths();
        try
        {
            var store = new SqliteArtifactStore(databasePath, artifactsRoot);
            var resolver = new ArtifactDownloadResolver(store);

            var result = await resolver.ResolveAsync("mission-1", "does-not-exist");

            Assert.IsType<ArtifactDownloadResult.NotFound>(result);
        }
        finally
        {
            CleanUp(databasePath, artifactsRoot);
        }
    }

    [Fact]
    public async Task ResolveAsync_ReturnsNotFound_ForArtifactBelongingToAnotherMission()
    {
        var (databasePath, artifactsRoot) = CreatePaths();
        try
        {
            var store = new SqliteArtifactStore(databasePath, artifactsRoot);
            var path = await store.SaveAsync("mission-1", "report.txt", ToStream("hello world"));
            var artifact = CreateArtifact("artifact-1", "mission-1", path, discardedAt: null);
            await store.SaveMissionArtifactAsync(artifact);
            var resolver = new ArtifactDownloadResolver(store);

            var result = await resolver.ResolveAsync("mission-2", "artifact-1");

            Assert.IsType<ArtifactDownloadResult.NotFound>(result);
        }
        finally
        {
            CleanUp(databasePath, artifactsRoot);
        }
    }

    private static MissionArtifact CreateArtifact(string artifactId, string missionId, string path, DateTimeOffset? discardedAt) => new()
    {
        ArtifactId = artifactId,
        MissionId = missionId,
        SourceMessageId = "message-1",
        Path = path,
        Summary = "summary",
        ContentHash = "hash",
        DiscardedAt = discardedAt,
    };

    private static Stream ToStream(string content) => new MemoryStream(Encoding.UTF8.GetBytes(content));

    private static (string DatabasePath, string ArtifactsRoot) CreatePaths()
    {
        var root = Path.Combine(Path.GetTempPath(), "work-agents-tests", $"{Guid.NewGuid():N}");
        return (Path.Combine(root, "state", "work-agents.db"), Path.Combine(root, "artifacts"));
    }

    private static void CleanUp(string databasePath, string artifactsRoot)
    {
        var stateDir = Path.GetDirectoryName(databasePath);
        var root = stateDir is not null ? Path.GetDirectoryName(stateDir) : null;
        if (root is not null && Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
        else if (Directory.Exists(artifactsRoot))
        {
            Directory.Delete(artifactsRoot, recursive: true);
        }
    }
}
