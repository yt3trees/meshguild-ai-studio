using WorkAgents.Core.Authoring;
using WorkAgents.Core.Graphs;
using WorkAgents.Core.Missions;
using WorkAgents.Core.Teams;

namespace WorkAgents.UnitTests.Authoring;

/// <summary>
/// 定義から組み立てる日本語の説明が、書き手の検算として役に立つことを確かめる (案C)。
/// </summary>
public sealed class DefinitionNarratorTests
{
    [Fact]
    public void Describe_Team_ListsRosterChannelsAndLimits()
    {
        var team = new TeamDefinition
        {
            Name = "review-team",
            Orchestrator = new TeamOrchestrator { Agent = "lead" },
            Members =
            [
                new TeamMember { Agent = "coder", Role = "実装" },
                new TeamMember { Agent = "tester", Role = "検証", MaxInstances = 2 },
            ],
            ChannelsAllow =
            [
                new ChannelRule { From = "tester", To = "coder", Kinds = [MessageKind.Question] },
            ],
            Limits = new TeamLimits { MaxDelegationDepth = 2, MaxParallelInstances = 4 },
        };

        var lines = DefinitionNarrator.Describe(team);
        var roster = lines.Single(line => line.Heading == "編成").Text;
        var channels = lines.Single(line => line.Heading == "会話").Text;
        var limits = lines.Single(line => line.Heading == "上限").Text;

        Assert.Contains("統括は lead", roster);
        Assert.Contains("coder (実装)", roster);
        Assert.Contains("tester は最大 2 体", roster);
        Assert.Contains("統括を経由", channels);
        Assert.Contains("tester から coder への質問", channels);
        Assert.Contains("2 段まで", limits);
    }

    [Fact]
    public void Describe_Team_SaysWhenDirectTalkIsTheDefault()
    {
        var team = new TeamDefinition
        {
            Name = "flat",
            Orchestrator = new TeamOrchestrator { Agent = "lead" },
            Members = [new TeamMember { Agent = "worker" }],
            ChannelsDefault = ChannelDefault.Direct,
        };

        var channels = DefinitionNarrator.Describe(team).Single(line => line.Heading == "会話").Text;

        Assert.Contains("直接会話できる", channels);
    }

    [Fact]
    public void Describe_Graph_NamesStartAndTerminalNodes()
    {
        var graph = LinearGraph();

        var lines = DefinitionNarrator.Describe(graph);

        Assert.Contains("plan", lines.Single(line => line.Heading == "開始").Text);
        Assert.Contains("report", lines.Single(line => line.Heading == "終了").Text);
    }

    [Fact]
    public void DescribeNode_ExplainsAgentNodeAndItsSuccessor()
    {
        var graph = LinearGraph();
        var node = graph.Nodes.Single(item => item.Id == "plan");

        var text = DefinitionNarrator.DescribeNode(node, graph);

        Assert.Contains("エージェント planner", text);
        Assert.Contains("report へ進む", text);
    }

    [Fact]
    public void DescribeNode_WarnsWhenBranchHasNoDefaultEdge()
    {
        var graph = new GraphDefinition
        {
            Name = "branchy",
            Nodes =
            [
                new GraphNode { Id = "check", Kind = NodeKind.Branch },
                new GraphNode { Id = "ok", Kind = NodeKind.Agent, Agent = "a" },
            ],
            Edges = [new GraphEdge { Id = "e1", From = "check", To = "ok", Condition = "${mission.goal} == 'x'" }],
        };

        var text = DefinitionNarrator.DescribeNode(graph.Nodes[0], graph);

        Assert.Contains("当てはまらなかったときの行き先が無い", text);
    }

    [Fact]
    public void DescribeNode_SpellsOutLoopStopConditions()
    {
        var graph = new GraphDefinition
        {
            Name = "looping",
            Nodes =
            [
                new GraphNode
                {
                    Id = "improve",
                    Kind = NodeKind.Loop,
                    Body = "body",
                    Stop = new LoopStopCondition { MaxIterations = 5, ScoreThreshold = 0.8 },
                    Evaluator = new NodeEvaluatorSpec { Kind = "agent", Agent = "judge" },
                },
            ],
            Edges = [],
        };

        var text = DefinitionNarrator.DescribeNode(graph.Nodes[0], graph);

        Assert.Contains("5 回まで", text);
        Assert.Contains("0.8 以上", text);
        Assert.Contains("judge", text);
        Assert.Contains("ここで終わる", text);
    }

    [Fact]
    public void Headline_SummarisesGraphSize()
    {
        var headline = DefinitionNarrator.Headline(LinearGraph());

        Assert.Contains("ノード 2 件", headline);
        Assert.Contains("plan から始まる", headline);
    }

    private static GraphDefinition LinearGraph() => new()
    {
        Name = "linear",
        Nodes =
        [
            new GraphNode { Id = "plan", Kind = NodeKind.Agent, Agent = "planner", Input = "${mission.goal}" },
            new GraphNode { Id = "report", Kind = NodeKind.Agent, Agent = "writer", Input = "${nodes.plan.output}" },
        ],
        Edges = [new GraphEdge { Id = "plan-to-report", From = "plan", To = "report" }],
    };
}
