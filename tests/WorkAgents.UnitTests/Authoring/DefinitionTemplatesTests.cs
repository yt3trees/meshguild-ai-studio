using WorkAgents.Agents.Loading;
using WorkAgents.Core.Authoring;
using WorkAgents.Orchestration.Graph;

namespace WorkAgents.UnitTests.Authoring;

/// <summary>
/// テンプレートが「そのまま検証を通る」ことを確かめる (案E)。
/// 雛形が最初から赤いと、初見の書き手はどこが自分のせいか分からなくなる。
/// </summary>
public sealed class DefinitionTemplatesTests
{
    private static readonly string[] KnownAgents =
    [
        "orchestrator", "implementer", "reviewer", "researcher", "writer", "worker",
        "planner", "summarizer", "drafter", "publisher", "reviser", "judge",
        "researcher-a", "researcher-b",
    ];

    [Fact]
    public void All_HaveDistinctIdsAndDescriptions()
    {
        var templates = DefinitionTemplates.All;

        Assert.NotEmpty(templates);
        Assert.Equal(templates.Count, templates.Select(template => template.Id).Distinct(StringComparer.Ordinal).Count());
        Assert.All(templates, template =>
        {
            Assert.NotEmpty(template.Title);
            Assert.NotEmpty(template.Summary);
            Assert.NotEmpty(template.WhenToUse);
            Assert.NotEmpty(template.Slots);
        });
    }

    [Theory]
    [InlineData("team-review")]
    [InlineData("team-research-write")]
    [InlineData("team-solo")]
    public void BuildTeam_ProducesDefinitionsThatPassTheLoader(string id)
    {
        var template = DefinitionTemplates.Find(id)!;
        var team = template.BuildTeam("sample-team");

        // ローダーが唯一の検証実装なので、書き出して読み直すことで確かめる。
        var folder = Path.Combine(Path.GetTempPath(), "wa-template-test", Guid.NewGuid().ToString("n"), "sample-team");
        Directory.CreateDirectory(folder);
        try
        {
            File.WriteAllText(Path.Combine(folder, "team.yaml"), new TeamYamlWriter().ToYaml(team));
            var loaded = new FileBasedTeamLoader().Load(folder, KnownAgents);

            Assert.Equal("sample-team", loaded.Name);
            Assert.NotEmpty(loaded.Members);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(folder)!, recursive: true);
        }
    }

    [Theory]
    [InlineData("graph-linear3")]
    [InlineData("graph-approval")]
    [InlineData("graph-quality-loop")]
    [InlineData("graph-parallel-join")]
    public void BuildGraph_ProducesDefinitionsThatPassValidation(string id)
    {
        var template = DefinitionTemplates.Find(id)!;
        var graph = template.BuildGraph("sample-graph") with
        {
            FolderPath = Path.Combine("graphs", "sample-graph"),
        };

        var result = new GraphValidator(KnownAgents, [], []).Validate(graph);

        Assert.True(result.IsValid, string.Join(" / ", result.ToDiagnostics().Select(d => d.ToDisplayLine())));
    }

    [Theory]
    [InlineData("graph-linear3")]
    [InlineData("graph-approval")]
    [InlineData("graph-quality-loop")]
    [InlineData("graph-parallel-join")]
    public void BuildGraph_RoundTripsThroughTheYamlWriter(string id)
    {
        var template = DefinitionTemplates.Find(id)!;
        var graph = template.BuildGraph("sample-graph");
        var folder = Path.Combine("graphs", "sample-graph");

        var yaml = new GraphYamlWriter().ToYaml(graph);
        var reloaded = new FileBasedGraphLoader().LoadText(yaml, folder);

        Assert.Equal(graph.Nodes.Count, reloaded.Nodes.Count);
        Assert.Equal(graph.Edges.Count, reloaded.Edges.Count);
        Assert.Equal(graph.Subgraphs.Count, reloaded.Subgraphs.Count);
    }

    [Fact]
    public void BuildTeam_UsesSuppliedSlotsInsteadOfPlaceholders()
    {
        var template = DefinitionTemplates.Find("team-solo")!;

        var team = template.BuildTeam("mine", new Dictionary<string, string>
        {
            ["lead"] = "reviewer",
            ["worker"] = "implementer",
        });

        Assert.Equal("reviewer", team.Orchestrator.Agent);
        Assert.Equal("implementer", Assert.Single(team.Members).Agent);
    }

    [Fact]
    public void BuildTeam_FallsBackToPlaceholdersForMissingSlots()
    {
        var template = DefinitionTemplates.Find("team-solo")!;

        var team = template.BuildTeam("mine");

        Assert.Equal("orchestrator", team.Orchestrator.Agent);
    }

    [Fact]
    public void BuildGraph_ThrowsWhenTemplateIsNotAGraph()
    {
        var template = DefinitionTemplates.Find("team-solo")!;

        Assert.Throws<InvalidOperationException>(() => template.BuildGraph("mine"));
    }
}
