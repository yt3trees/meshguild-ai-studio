using WorkAgents.Agents.Configuration;
using WorkAgents.Agents.Loading;

namespace WorkAgents.UnitTests.Loading;

public sealed class SharedSkillCatalogTests
{
    [Fact]
    public void List_ReturnsSkillsOnDisk_EvenWhenNoAgentReferencesThem()
    {
        var root = CreateRoot();
        try
        {
            WriteSkill(root, "meeting-minutes", "議事録を整形する規約。");
            WriteSkill(root, "release-notes", description: null);

            var skills = SharedSkillCatalog.List(root);

            Assert.Equal(new[] { "meeting-minutes", "release-notes" }, skills.Select(skill => skill.Name));
            Assert.Equal("議事録を整形する規約。", skills[0].Description);
            Assert.Null(skills[1].Description);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void List_IgnoresFoldersWithoutSkillFile()
    {
        var root = CreateRoot();
        try
        {
            WriteSkill(root, "usable", "使えるスキル。");
            Directory.CreateDirectory(Path.Combine(root, "skills", "empty-folder"));

            var skills = SharedSkillCatalog.List(root);

            Assert.Equal(new[] { "usable" }, skills.Select(skill => skill.Name));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void List_ReturnsEmpty_WhenSkillsDirectoryIsMissing()
    {
        var root = CreateRoot();
        try
        {
            Assert.Empty(SharedSkillCatalog.List(root));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ListFromSources_MergesSkillsAndLaterSourceWins()
    {
        var root = CreateRoot();
        try
        {
            var standard = Path.Combine(root, "standard");
            var team = Path.Combine(root, "team-sales");
            WriteSkill(standard, "shared", "標準スキル。");
            WriteSkill(standard, "standard-only", "標準専用スキル。");
            WriteSkill(team, "shared", "チーム版スキル。");
            WriteSkill(team, "team-only", "チーム専用スキル。");

            var skills = SharedSkillCatalog.ListFromSources([
                new DefinitionSourceEntry { Label = "standard", Path = standard },
                new DefinitionSourceEntry { Label = "team-sales", Path = team },
            ]);

            Assert.Equal(["shared", "standard-only", "team-only"], skills.Select(skill => skill.Name));
            Assert.Equal("チーム版スキル。", skills.Single(skill => skill.Name == "shared").Description);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string CreateRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "work-agents-tests", $"{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        return root;
    }

    private static void WriteSkill(string sourceRoot, string name, string? description)
    {
        var dir = Path.Combine(sourceRoot, "skills", name);
        Directory.CreateDirectory(dir);
        var frontmatter = description is null
            ? $"""
                ---
                name: {name}
                ---
                """
            : $"""
                ---
                name: {name}
                description: {description}
                ---
                """;
        File.WriteAllText(Path.Combine(dir, "SKILL.md"), $"{frontmatter}\n\n# {name}\n");
    }
}
