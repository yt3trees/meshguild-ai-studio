namespace WorkAgents.Core.Abstractions;

/// <summary>
/// 成果物の書き込み・回収(5.12)。
/// Local: NTFS / Azure: Blob。Blob 実装は削除ツールを無効化。
/// </summary>
public interface IArtifactStore
{
    Task<string> SaveAsync(
        string purpose,
        string fileName,
        Stream content,
        CancellationToken ct = default);

    Task<Stream?> OpenReadAsync(string uri, CancellationToken ct = default);
}