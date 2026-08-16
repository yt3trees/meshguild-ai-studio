using WorkAgents.Core.Authoring;
using WorkAgents.Core.Graphs;
using WorkAgents.Orchestration.Graph;

namespace WorkAgents.UnitTests.Authoring;

/// <summary>
/// 検証エラーが「原因」と「直し方」の 2 つを日本語で返すことを確かめる (案D)。
/// エラーコードや英文がそのまま画面に出ると、初見の書き手は手が止まる。
/// </summary>
public sealed class ValidationMessageCatalogTests
{
    [Fact]
    public void ForGraph_ExplainsMissingStopConditionWithFix()
    {
        var diagnostic = ValidationMessageCatalog.ForGraph(
            "missing_stop_condition",
            "Loop 'improve' needs a stop condition.",
            ["improve"]);

        Assert.Contains("improve", diagnostic.Message);
        Assert.Contains("停止条件", diagnostic.Message);
        Assert.NotNull(diagnostic.Fix);
        Assert.Contains("maxIterations", diagnostic.Fix!);
        Assert.Equal(["improve"], diagnostic.NodeIds);
    }

    [Fact]
    public void ForGraph_DistinguishesDuplicateNodeAndEdgeIds()
    {
        var node = ValidationMessageCatalog.ForGraph("duplicate_id", "Node IDs must be unique.", ["plan"], []);
        var edge = ValidationMessageCatalog.ForGraph("duplicate_id", "Edge IDs must be unique.", [], ["plan-to-work"]);

        Assert.Contains("ノード ID", node.Message);
        Assert.Contains("エッジ ID", edge.Message);
    }

    [Fact]
    public void ForGraph_NamesTheReferencedDefinitionKind()
    {
        var diagnostic = ValidationMessageCatalog.ForGraph(
            "unknown_definition_ref",
            "Node 'plan' references an unknown agent.",
            ["plan"]);

        Assert.Contains("エージェント", diagnostic.Message);
    }

    [Fact]
    public void ForGraph_QuotesTheUnresolvedReference()
    {
        var diagnostic = ValidationMessageCatalog.ForGraph(
            "unresolved_reference",
            "Reference 'nodes.typo.output' cannot be resolved.",
            ["work"]);

        Assert.Contains("${nodes.typo.output}", diagnostic.Message);
    }

    [Fact]
    public void ForGraph_KeepsUnknownCodesReadableInsteadOfSwallowingThem()
    {
        var diagnostic = ValidationMessageCatalog.ForGraph("something_new", "Raw message from a future rule.");

        Assert.Equal("Raw message from a future rule.", diagnostic.Message);
        Assert.Equal("Raw message from a future rule.", diagnostic.RawMessage);
    }

    [Fact]
    public void ForTeam_ExplainsUnknownAgentWithTheName()
    {
        var diagnostic = ValidationMessageCatalog.ForTeam("unknown agent: reviewer");

        Assert.Equal("unknown_agent", diagnostic.Code);
        Assert.Contains("reviewer", diagnostic.Message);
        Assert.Contains("agents/", diagnostic.Fix!);
    }

    [Fact]
    public void ForTeam_SuggestsMaxInstancesForDuplicateMembers()
    {
        var diagnostic = ValidationMessageCatalog.ForTeam("duplicate member: coder");

        Assert.Contains("coder", diagnostic.Message);
        Assert.Contains("maxInstances", diagnostic.Fix!);
    }

    [Fact]
    public void ForTeam_ExplainsParallelLimitConflict()
    {
        var diagnostic = ValidationMessageCatalog.ForTeam("member instances exceed team parallel limit");

        Assert.Equal("parallel_limit_exceeded", diagnostic.Code);
        Assert.Contains("maxParallelInstances", diagnostic.Message);
        Assert.NotNull(diagnostic.Fix);
    }

    [Fact]
    public void ToDiagnostics_TranslatesEveryValidatorError()
    {
        // 停止条件のないループと、合流方針のない join を含む壊れたグラフ。
        var graph = new GraphDefinition
        {
            Name = "broken",
            FolderPath = Path.Combine("graphs", "broken"),
            Nodes =
            [
                new GraphNode { Id = "start", Kind = NodeKind.Agent, Agent = "planner" },
                new GraphNode { Id = "spin", Kind = NodeKind.Loop, Body = "body" },
                new GraphNode { Id = "merge", Kind = NodeKind.Join },
            ],
            Edges =
            [
                new GraphEdge { Id = "e1", From = "start", To = "spin" },
                new GraphEdge { Id = "e2", From = "spin", To = "merge" },
            ],
        };

        var diagnostics = new GraphValidator().Validate(graph).ToDiagnostics();

        Assert.Contains(diagnostics, diagnostic => diagnostic.Code == "missing_stop_condition");
        Assert.Contains(diagnostics, diagnostic => diagnostic.Code == "missing_join_policy");
        Assert.All(diagnostics, diagnostic => Assert.NotEmpty(diagnostic.Message));
    }

    [Fact]
    public void ForAgent_ExplainsUnknownSkillWithFix()
    {
        var diagnostic = ValidationMessageCatalog.ForAgent("unknown skill: meeting-minutes");

        Assert.Equal("unknown_skill", diagnostic.Code);
        Assert.Contains("meeting-minutes", diagnostic.Message);
        Assert.NotNull(diagnostic.Fix);
        Assert.Contains("SKILL.md", diagnostic.Fix!);
    }

    [Fact]
    public void ForAgent_ExplainsInvalidNameAndFileStore()
    {
        var name = ValidationMessageCatalog.ForAgent("invalid agent name");
        var fileStore = ValidationMessageCatalog.ForAgent("unknown fileStore");

        Assert.Equal("invalid_agent_name", name.Code);
        Assert.Contains("name", name.Message);
        Assert.Equal("unknown_file_store", fileStore.Code);
        Assert.Contains("workspace", fileStore.Fix!);
    }
}
