using Microsoft.Extensions.Logging;
using WorkAgents.Agents.Configuration;

namespace WorkAgents.Agents.Loading;

/// <summary>
/// 複数の <see cref="DefinitionSourceEntry"/> を後勝ちでマージ解決した1件分(data-model.md「解決済み定義」)。
/// </summary>
public sealed record ResolvedDefinitionFolder
{
    public required string Name { get; init; }

    public required string FolderPath { get; init; }

    public required string SourceLabel { get; init; }

    public IReadOnlyList<string> OverriddenSourceLabels { get; init; } = [];
}

/// <summary>1つのサブフォルダ種別(<c>agents</c>等)を解決した際の診断サマリー。</summary>
public sealed record DefinitionSourceResolutionSummary
{
    public required string SubFolderName { get; init; }

    public IReadOnlyDictionary<string, int> LoadedCountsByLabel { get; init; } = new Dictionary<string, int>(StringComparer.Ordinal);

    public IReadOnlyList<string> SkippedSourceLabels { get; init; } = [];

    public int OverrideCount { get; init; }
}

/// <summary>
/// <see cref="DefinitionSourceEntry"/> の順序付きリストを受け取り、指定したサブフォルダ
/// (<c>agents</c>/<c>teams</c>/<c>graphs</c>/<c>workflows</c>)配下の同名定義フォルダを後勝ちで
/// マージ解決する共通リゾルバー(data-model.md「定義ソース構成」「解決済み定義」)。
/// 存在しない <c>Path</c> はスキップして継続する(FR-006)。
/// </summary>
public sealed class DefinitionSourceResolver
{
    private readonly IReadOnlyList<DefinitionSourceEntry> _sources;
    private readonly ILogger<DefinitionSourceResolver>? _logger;

    public DefinitionSourceResolver(IReadOnlyList<DefinitionSourceEntry> sources, ILogger<DefinitionSourceResolver>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(sources);
        if (sources.Count == 0)
        {
            throw new ArgumentException("At least one definition source is required.", nameof(sources));
        }

        var duplicateLabel = sources
            .GroupBy(source => source.Label, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicateLabel is not null)
        {
            throw new ArgumentException($"Duplicate definition source label: '{duplicateLabel.Key}'.", nameof(sources));
        }

        _sources = sources;
        _logger = logger;
    }

    /// <summary>
    /// <paramref name="subFolderName"/>(例: <c>agents</c>)配下のフォルダを全ソースから走査し、
    /// 同名フォルダは後から読み込んだソース側を優先してマージする。
    /// </summary>
    public (IReadOnlyList<ResolvedDefinitionFolder> Folders, DefinitionSourceResolutionSummary Summary) ResolveFolders(string subFolderName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subFolderName);

        var byName = new Dictionary<string, ResolvedDefinitionFolder>(StringComparer.OrdinalIgnoreCase);
        var loadedCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        var skipped = new List<string>();
        var overrideCount = 0;

        foreach (var source in _sources)
        {
            var root = System.IO.Path.Combine(source.Path, subFolderName);
            if (!Directory.Exists(root))
            {
                _logger?.LogWarning(
                    "definition source '{Label}' has no '{SubFolder}' directory, skipping: {Root}",
                    source.Label, subFolderName, root);
                skipped.Add(source.Label);
                loadedCounts[source.Label] = 0;
                continue;
            }

            var countForSource = 0;
            foreach (var dir in Directory.EnumerateDirectories(root))
            {
                var name = System.IO.Path.GetFileName(dir);
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                countForSource++;
                if (byName.TryGetValue(name, out var existing))
                {
                    _logger?.LogWarning(
                        "definition '{Name}' ({SubFolder}) from source '{Label}' overrides source '{Prior}'",
                        name, subFolderName, source.Label, existing.SourceLabel);
                    overrideCount++;
                    byName[name] = existing with
                    {
                        FolderPath = dir,
                        SourceLabel = source.Label,
                        OverriddenSourceLabels = [.. existing.OverriddenSourceLabels, existing.SourceLabel],
                    };
                }
                else
                {
                    byName[name] = new ResolvedDefinitionFolder
                    {
                        Name = name,
                        FolderPath = dir,
                        SourceLabel = source.Label,
                    };
                }
            }

            loadedCounts[source.Label] = countForSource;
        }

        var summary = new DefinitionSourceResolutionSummary
        {
            SubFolderName = subFolderName,
            LoadedCountsByLabel = loadedCounts,
            SkippedSourceLabels = skipped,
            OverrideCount = overrideCount,
        };

        _logger?.LogInformation(
            "resolved {Count} '{SubFolder}' definition(s) from {SourceCount} source(s), {OverrideCount} override(s), {SkippedCount} source(s) skipped",
            byName.Count, subFolderName, _sources.Count, overrideCount, skipped.Count);

        return (byName.Values.ToArray(), summary);
    }
}
