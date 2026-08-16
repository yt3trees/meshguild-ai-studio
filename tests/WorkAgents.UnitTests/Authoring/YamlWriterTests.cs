using WorkAgents.Agents.Loading;
using WorkAgents.Core.Graphs;
using WorkAgents.Core.Missions;
using WorkAgents.Core.Teams;

namespace WorkAgents.UnitTests.Authoring;

/// <summary>
/// GUI からの保存で項目が落ちないことを確かめる。
/// 書き出して読み直したときに値が消えると、画面で編集するたびに定義が痩せていく。
/// </summary>
public sealed class YamlWriterTests
{
    [Fact]
    public void GraphWriter_KeepsEvaluatorTimeoutAndSubgraphs()
    {
        var graph = new GraphDefinition
        {
            Name = "full",
            DisplayName = "全部入り",
            Description = "書き出しの取りこぼしを見るための定義。",
            Defaults = new GraphDefaults { Team = "review-team", BudgetCostLimitUsd = 2.5, BudgetTimeLimitSeconds = 600 },
            Nodes =
            [
                new GraphNode { Id = "gate", Kind = NodeKind.Approval, Title = "承認", Summary = "内容を確認", TimeoutSeconds = 120 },
                new GraphNode
                {
                    Id = "improve",
                    Kind = NodeKind.Loop,
                    Body = "body",
                    Stop = new LoopStopCondition { MaxIterations = 4, CostLimitUsd = 1.25, TimeLimitSeconds = 300, ScoreThreshold = 0.9 },
                    Evaluator = new NodeEvaluatorSpec
                    {
                        Kind = "deterministic",
                        Node = "metrics",
                        Metrics = [new MetricTarget { Name = "coverage", Target = 0.85, Direction = "gte" }],
                    },
                },
            ],
            Edges = [new GraphEdge { Id = "gate-to-improve", From = "gate", To = "improve" }],
            Subgraphs = new Dictionary<string, SubgraphDefinition>(StringComparer.Ordinal)
            {
                ["body"] = new SubgraphDefinition
                {
                    Nodes = [new GraphNode { Id = "revise", Kind = NodeKind.Agent, Agent = "reviser", Input = "${loop.previous.output}" }],
                    Edges = [],
                },
            },
            Layout = new Dictionary<string, (double X, double Y)>(StringComparer.Ordinal) { ["gate"] = (80, 40) },
        };

        var reloaded = new FileBasedGraphLoader().LoadText(
            new GraphYamlWriter().ToYaml(graph),
            Path.Combine("graphs", "full"));

        var gate = reloaded.Nodes.Single(node => node.Id == "gate");
        Assert.Equal(120, gate.TimeoutSeconds);

        var improve = reloaded.Nodes.Single(node => node.Id == "improve");
        Assert.Equal(4, improve.Stop!.MaxIterations);
        Assert.Equal(1.25, improve.Stop.CostLimitUsd);
        Assert.Equal(300, improve.Stop.TimeLimitSeconds);
        Assert.Equal(0.9, improve.Stop.ScoreThreshold);
        Assert.Equal("deterministic", improve.Evaluator!.Kind);
        Assert.Equal("metrics", improve.Evaluator.Node);
        Assert.Equal("coverage", Assert.Single(improve.Evaluator.Metrics).Name);

        Assert.Single(reloaded.Subgraphs);
        Assert.Equal("reviser", reloaded.Subgraphs["body"].Nodes.Single().Agent);
        Assert.Equal("review-team", reloaded.Defaults!.Team);
        Assert.Equal(2.5, reloaded.Defaults.BudgetCostLimitUsd);
        Assert.Equal((80d, 40d), reloaded.Layout["gate"]);
    }

    [Fact]
    public void GraphWriter_EscapesNewlinesSoIndentationSurvives()
    {
        var graph = new GraphDefinition
        {
            Name = "multiline",
            Nodes = [new GraphNode { Id = "plan", Kind = NodeKind.Agent, Agent = "planner", Input = "一行目\n二行目: コロン入り" }],
            Edges = [],
        };

        var yaml = new GraphYamlWriter().ToYaml(graph);
        var reloaded = new FileBasedGraphLoader().LoadText(yaml, Path.Combine("graphs", "multiline"));

        Assert.Equal("一行目\n二行目: コロン入り", reloaded.Nodes[0].Input);
    }

    [Fact]
    public void TeamWriter_RoundTripsThroughTheLoader()
    {
        var team = new TeamDefinition
        {
            Name = "round-trip",
            DisplayName = "往復",
            Description = "書いて読み直す。",
            Orchestrator = new TeamOrchestrator { Agent = "lead", MaxInstances = 2 },
            Members =
            [
                new TeamMember { Agent = "coder", Role = "実装", Scope = "src 配下", MaxInstances = 2 },
                new TeamMember { Agent = "tester", Role = "検証" },
            ],
            ChannelsDefault = ChannelDefault.Direct,
            ChannelsAllow = [new ChannelRule { From = "tester", To = "coder", Kinds = [MessageKind.Question, MessageKind.Share] }],
            Limits = new TeamLimits { MaxDelegationDepth = 4, MaxParallelInstances = 8, NoProgressRoundTrips = 7, AskTimeoutSeconds = 120 },
            Evaluation = new TeamEvaluationDefaults { Evaluator = "judge", ScoreThreshold = 0.75 },
        };

        var folder = Path.Combine(Path.GetTempPath(), "wa-team-writer", Guid.NewGuid().ToString("n"), "round-trip");
        Directory.CreateDirectory(folder);
        try
        {
            File.WriteAllText(Path.Combine(folder, "team.yaml"), new TeamYamlWriter().ToYaml(team));
            var reloaded = new FileBasedTeamLoader().Load(folder, ["lead", "coder", "tester", "judge"]);

            Assert.Equal(2, reloaded.Orchestrator.MaxInstances);
            Assert.Equal("src 配下", reloaded.Members[0].Scope);
            Assert.Equal(2, reloaded.Members[0].MaxInstances);
            Assert.Equal(ChannelDefault.Direct, reloaded.ChannelsDefault);
            Assert.Equal([MessageKind.Question, MessageKind.Share], Assert.Single(reloaded.ChannelsAllow).Kinds);
            Assert.Equal(4, reloaded.Limits.MaxDelegationDepth);
            Assert.Equal(120, reloaded.Limits.AskTimeoutSeconds);
            Assert.Equal(0.75, reloaded.Evaluation!.ScoreThreshold);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(folder)!, recursive: true);
        }
    }

    [Fact]
    public void TeamWriter_OmitsValuesThatMatchTheDefaults()
    {
        var team = new TeamDefinition
        {
            Name = "lean",
            Orchestrator = new TeamOrchestrator { Agent = "lead" },
            Members = [new TeamMember { Agent = "worker" }],
        };

        var yaml = new TeamYamlWriter().ToYaml(team);

        // 既定のままの項目を書かないことで、書いてある行 = 意図して変えた行になる。
        Assert.DoesNotContain("limits:", yaml);
        Assert.DoesNotContain("channels:", yaml);
        Assert.DoesNotContain("maxInstances", yaml);
        Assert.DoesNotContain("evaluation:", yaml);
    }

    [Fact]
    public void AgentWriter_WritesHarnessAndSkills()
    {
        var agent = new AgentDefinition
        {
            Name = "coder",
            DisplayName = "実装担当",
            Description = "コードを書く。",
            SharedSkillNames = ["review-checklist"],
            HarnessShell = true,
            HarnessFileStore = "workspace",
        };

        var yaml = new AgentYamlWriter().ToYaml(agent);

        Assert.Contains("name: \"coder\"", yaml);
        Assert.Contains("- \"review-checklist\"", yaml);
        Assert.Contains("shell: true", yaml);
        Assert.Contains("fileStore: \"workspace\"", yaml);
    }

    [Fact]
    public void AgentWriter_RoundTripsKind()
    {
        var yaml = new AgentYamlWriter().ToYaml(new AgentDefinition { Name = "coder", Kind = "Prompt" });

        // GUI から保存しても、参考用の kind を書き落とさない。
        Assert.Contains("kind: \"Prompt\"", yaml);
    }

    [Fact]
    public void AgentWriter_OmitsKind_WhenNotSet()
    {
        var yaml = new AgentYamlWriter().ToYaml(new AgentDefinition { Name = "coder" });

        Assert.DoesNotContain("kind:", yaml);
    }
}
