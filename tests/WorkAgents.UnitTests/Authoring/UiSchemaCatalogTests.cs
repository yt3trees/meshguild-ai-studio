using WorkAgents.Core.Authoring;

namespace WorkAgents.UnitTests.Authoring;

/// <summary>
/// GUI のフォームがスキーマだけを頼りに組み立てられることを確かめる (案A/B)。
/// ここが壊れると、schemas/*.json を直したのに画面が追随しないという食い違いが起きる。
/// </summary>
public sealed class UiSchemaCatalogTests
{
    [Theory]
    [InlineData("agent")]
    [InlineData("team")]
    [InlineData("graph")]
    [InlineData("workspace")]
    [InlineData("workflow")]
    public void Get_LoadsEveryEmbeddedSchema(string id)
    {
        var document = UiSchemaCatalog.Get(id);

        Assert.Equal(id, document.Id);
        Assert.NotEmpty(document.Title);
        Assert.NotEmpty(document.Fields);
    }

    [Fact]
    public void Get_MarksRequiredRootFields()
    {
        var team = UiSchemaCatalog.Get("team");

        Assert.True(team.Field("name")!.Required);
        Assert.True(team.Field("orchestrator")!.Required);
        Assert.False(team.Field("displayName")!.Required);
    }

    [Fact]
    public void Get_ReadsDescriptionAsHelpText()
    {
        var field = UiSchemaCatalog.Get("team").Field("orchestrator")!.Field("agent")!;

        Assert.Equal("統括を担うエージェント名。", field.Description);
    }

    [Fact]
    public void Get_ResolvesReferenceSourceForAgentFields()
    {
        var team = UiSchemaCatalog.Get("team");

        Assert.Equal("agents", team.Field("orchestrator")!.Field("agent")!.Source);
        Assert.Equal("agents", team.Field("members")!.Item!.Field("agent")!.Source);
        Assert.Equal("team-agents", team.Field("channels")!.Field("allow")!.Item!.Field("from")!.Source);
    }

    [Fact]
    public void Get_ResolvesRefsInsideDefinitions()
    {
        var graph = UiSchemaCatalog.Get("graph");

        // nodes は $ref: #/definitions/node なので、解決されて子フィールドを持つはず。
        var nodes = graph.Field("nodes")!;
        Assert.Equal(UiFieldType.Array, nodes.Type);
        Assert.NotNull(nodes.Item);
        Assert.Contains(nodes.Item!.Fields, field => field.Name == "kind");
    }

    [Fact]
    public void Get_ExposesEnumLabelsForNodeKind()
    {
        var kind = UiSchemaCatalog.Get("graph").Definition("node")!.Field("kind")!;

        Assert.Contains("loop", kind.EnumValues);
        Assert.StartsWith("loop (", kind.LabelFor("loop"));
        Assert.Equal("unknown", kind.LabelFor("unknown"));
    }

    [Fact]
    public void Get_TreatsFreeKeyObjectsAsMaps()
    {
        var subgraphs = UiSchemaCatalog.Get("graph").Field("subgraphs")!;

        Assert.Equal(UiFieldType.Map, subgraphs.Type);
        Assert.NotNull(subgraphs.Item);
    }

    [Fact]
    public void Get_HidesGeneratedAndConstantFields()
    {
        var graph = UiSchemaCatalog.Get("graph");

        Assert.True(graph.Field("version")!.IsHidden);
        Assert.True(graph.Field("layout")!.IsHidden);
    }

    [Fact]
    public void Get_FlagsDeprecatedSchema()
    {
        Assert.True(UiSchemaCatalog.Get("workflow").Deprecated);
        Assert.False(UiSchemaCatalog.Get("graph").Deprecated);
    }

    [Fact]
    public void IsVisible_ShowsLoopOnlyFieldsForLoopNodes()
    {
        var node = UiSchemaCatalog.Get("graph").Definition("node")!;
        var body = node.Field("body")!;

        Assert.True(body.IsVisible(name => name == "kind" ? "loop" : null));
        Assert.False(body.IsVisible(name => name == "kind" ? "agent" : null));
    }

    [Fact]
    public void IsVisible_RequiresEveryConditionToMatch()
    {
        // alternate は kind: join かつ onPartialFailure: alternate のときだけ意味を持つ。
        var alternate = UiSchemaCatalog.Get("graph").Definition("node")!.Field("alternate")!;

        Assert.True(alternate.IsVisible(name => name switch
        {
            "kind" => "join",
            "onPartialFailure" => "alternate",
            _ => null,
        }));
        Assert.False(alternate.IsVisible(name => name switch
        {
            "kind" => "join",
            "onPartialFailure" => "continue",
            _ => null,
        }));
    }

    [Fact]
    public void FieldsInGroup_OrdersByDeclaredOrder()
    {
        var basics = UiSchemaCatalog.Get("team").FieldsInGroup("基本");

        Assert.Equal(["name", "displayName", "description"], basics.Select(field => field.Name));
    }
}
