using WorkAgents.Core.Authoring;
using WorkAgents.Core.Graphs;
using WorkAgents.Core.Teams;

namespace WorkAgents.UnitTests.Authoring;

/// <summary>
/// 参照項目をドロップダウンにするための選択肢解決 (案A)。
/// ここが正しく引けないと、GUI は結局自由入力に戻ってしまう。
/// </summary>
public sealed class ReferenceOptionsTests
{
    [Fact]
    public void For_ReturnsRegisteredDefinitions()
    {
        var options = new ReferenceOptions
        {
            Agents = [new ReferenceOption("planner"), new ReferenceOption("writer")],
            Teams = [new ReferenceOption("review-team")],
        };

        Assert.Equal(["planner", "writer"], options.For("agents").Select(option => option.Value));
        Assert.Equal(["review-team"], options.For("teams").Select(option => option.Value));
    }

    [Fact]
    public void For_ResolvesNodeIdsFromTheGraphBeingEdited()
    {
        var options = new ReferenceOptions { Graph = SampleGraph() };

        Assert.Equal(["plan", "score", "merge"], options.For("nodes").Select(option => option.Value));
    }

    [Fact]
    public void For_NarrowsCodeNodesForDeterministicEvaluators()
    {
        var options = new ReferenceOptions { Graph = SampleGraph() };

        Assert.Equal(["score"], options.For("code-nodes").Select(option => option.Value));
    }

    [Fact]
    public void For_ResolvesSubgraphIds()
    {
        var options = new ReferenceOptions { Graph = SampleGraph() };

        Assert.Equal(["body"], options.For("subgraphs").Select(option => option.Value));
    }

    [Fact]
    public void For_LimitsChannelEndpointsToTheTeamRoster()
    {
        var options = new ReferenceOptions
        {
            Team = new TeamDefinition
            {
                Name = "team",
                Orchestrator = new TeamOrchestrator { Agent = "lead" },
                Members = [new TeamMember { Agent = "coder", Role = "実装" }],
            },
        };

        var values = options.For("team-agents");

        Assert.Equal(["lead", "coder"], values.Select(option => option.Value));
        Assert.Equal("統括", values[0].Detail);
        Assert.Equal("実装", values[1].Detail);
    }

    [Fact]
    public void For_ReturnsEmptyForUnknownSourcesSoTheFormCanFallBack()
    {
        var options = new ReferenceOptions();

        Assert.Empty(options.For("something-else"));
        Assert.Empty(options.For(null));
    }

    [Fact]
    public void Contains_TreatsEmptyOptionListAsUnverifiable()
    {
        var options = new ReferenceOptions();

        // 選択肢を持っていない状態で「存在しない」と断じるのは誤検出になる。
        Assert.True(options.Contains("agents", "anything"));
    }

    [Fact]
    public void Contains_DetectsDanglingReferences()
    {
        var options = new ReferenceOptions { Agents = [new ReferenceOption("planner")] };

        Assert.True(options.Contains("agents", "planner"));
        Assert.False(options.Contains("agents", "typo"));
        Assert.True(options.Contains("agents", null));
    }

    private static GraphDefinition SampleGraph() => new()
    {
        Name = "sample",
        Nodes =
        [
            new GraphNode { Id = "plan", Kind = NodeKind.Agent, Agent = "planner" },
            new GraphNode { Id = "score", Kind = NodeKind.Code, CodeFile = "scripts/score.cs" },
            new GraphNode { Id = "merge", Kind = NodeKind.Join, JoinPolicy = JoinPolicy.All },
        ],
        Edges = [],
        Subgraphs = new Dictionary<string, SubgraphDefinition>(StringComparer.Ordinal)
        {
            ["body"] = new SubgraphDefinition { Nodes = [], Edges = [] },
        },
    };
}
