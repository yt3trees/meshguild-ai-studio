using WorkAgents.Agents.Configuration;

namespace WorkAgents.Agents.Loading;

/// <summary>共有スキル 1 件。<see cref="Name"/> がそのまま agent.yaml の <c>skills</c> に書かれる。</summary>
public sealed record SharedSkillInfo(string Name, string? Description);

/// <summary>共有スキルの実体フォルダーと、採用された定義ソース。</summary>
public sealed record SharedSkillLocation(string Name, string FolderPath, string SourceLabel);

/// <summary>
/// 共有スキル (<c>skills/&lt;name&gt;/SKILL.md</c>) をディスクから列挙する。
/// GUI の選択肢を「実際に置かれている SKILL.md」から作るためのもので、
/// 既存の agent.yaml が参照しているかどうかとは無関係に拾う。
/// 複数ソースでは後から列挙したソースが同名スキルを上書きする。
/// </summary>
public static class SharedSkillCatalog
{
    /// <summary>指定した定義ソースルート配下の <c>skills/</c> を走査する。名前昇順。</summary>
    public static IReadOnlyList<SharedSkillInfo> List(string sourceRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceRoot);
        return ListFromSources([
            new DefinitionSourceEntry { Label = "standard", Path = sourceRoot },
        ]);
    }

    /// <summary>
    /// 複数の定義ソースから共有スキルを列挙する。同名スキルは後勝ちで解決する。
    /// </summary>
    public static IReadOnlyList<SharedSkillInfo> ListFromSources(
        IReadOnlyList<DefinitionSourceEntry> sources)
        => ResolveLocations(sources)
            .Select(location => new SharedSkillInfo(
                location.Name,
                ReadDescription(Path.Combine(location.FolderPath, "SKILL.md"))))
            .OrderBy(skill => skill.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    /// <summary>
    /// 共有スキル名から実体フォルダーを解決する。同名スキルは後勝ちで解決する。
    /// </summary>
    public static IReadOnlyList<SharedSkillLocation> ResolveLocations(
        IReadOnlyList<DefinitionSourceEntry> sources)
    {
        ArgumentNullException.ThrowIfNull(sources);

        var byName = new Dictionary<string, SharedSkillLocation>(StringComparer.OrdinalIgnoreCase);
        foreach (var source in sources)
        {
            if (string.IsNullOrWhiteSpace(source.Path))
            {
                continue;
            }

            var root = Path.Combine(source.Path, "skills");
            if (!Directory.Exists(root))
            {
                continue;
            }

            foreach (var directory in Directory.EnumerateDirectories(root))
            {
                var skillFile = Path.Combine(directory, "SKILL.md");
                var name = Path.GetFileName(directory);
                if (!File.Exists(skillFile) || string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                byName[name] = new SharedSkillLocation(
                    name,
                    Path.GetFullPath(directory),
                    string.IsNullOrWhiteSpace(source.Label) ? "standard" : source.Label);
            }
        }

        return byName.Values
            .OrderBy(skill => skill.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <summary>
    /// SKILL.md 冒頭の frontmatter から <c>description</c> を拾う。
    /// 選択肢の補足に出すだけなので、無くても取れなくても支障はない。
    /// </summary>
    private static string? ReadDescription(string skillFile)
    {
        try
        {
            var lines = File.ReadLines(skillFile).Take(20).ToArray();
            if (lines.Length == 0 || lines[0].Trim() != "---")
            {
                return null;
            }

            foreach (var line in lines.Skip(1))
            {
                if (line.Trim() == "---")
                {
                    break;
                }
                if (line.StartsWith("description:", StringComparison.OrdinalIgnoreCase))
                {
                    var value = line["description:".Length..].Trim().Trim('"', '\'');
                    return string.IsNullOrWhiteSpace(value) ? null : value;
                }
            }
        }
        catch (IOException)
        {
            // 読めないものは説明なしとして扱う。列挙自体は続ける。
        }

        return null;
    }
}
