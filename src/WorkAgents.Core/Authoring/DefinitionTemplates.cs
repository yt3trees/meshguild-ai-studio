using WorkAgents.Core.Graphs;
using WorkAgents.Core.Missions;
using WorkAgents.Core.Teams;

namespace WorkAgents.Core.Authoring;

/// <summary>テンプレートが対象とする定義の種類。</summary>
public enum DefinitionKind
{
    Agent,
    Team,
    Graph,
}

/// <summary>
/// テンプレートを実体化するときに、書き手が埋める必要のある箇所 1 つ。
/// <see cref="Source"/> は選択肢の取得元 (既存のエージェント / チーム) を表す。
/// </summary>
public sealed record TemplateSlot
{
    public required string Key { get; init; }

    public required string Label { get; init; }

    public required string Description { get; init; }

    public DefinitionKind Source { get; init; } = DefinitionKind.Agent;

    /// <summary>選択されなかったときに埋める仮の名前。検証で弾かれるため、そのままでは保存できない。</summary>
    public required string Placeholder { get; init; }
}

/// <summary>
/// 白紙から書かせないための雛形 (案E)。
/// 実体は <see cref="TeamDefinition"/> / <see cref="GraphDefinition"/> を返す組み立て関数で、
/// YAML 文字列ではないため、テンプレート自体がモデルの制約から外れることがない。
/// </summary>
public sealed record DefinitionTemplate
{
    public required string Id { get; init; }

    public required DefinitionKind Kind { get; init; }

    public required string Title { get; init; }

    /// <summary>何を作る雛形か。</summary>
    public required string Summary { get; init; }

    /// <summary>どういうときに選ぶか。迷ったときの判断材料。</summary>
    public required string WhenToUse { get; init; }

    public IReadOnlyList<TemplateSlot> Slots { get; init; } = Array.Empty<TemplateSlot>();

    internal Func<string, IReadOnlyDictionary<string, string>, TeamDefinition>? TeamFactory { get; init; }

    internal Func<string, IReadOnlyDictionary<string, string>, GraphDefinition>? GraphFactory { get; init; }

    public TeamDefinition BuildTeam(string name, IReadOnlyDictionary<string, string>? slots = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (TeamFactory is null)
        {
            throw new InvalidOperationException($"template '{Id}' does not produce a team definition.");
        }
        return TeamFactory(name, Resolve(slots));
    }

    public GraphDefinition BuildGraph(string name, IReadOnlyDictionary<string, string>? slots = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (GraphFactory is null)
        {
            throw new InvalidOperationException($"template '{Id}' does not produce a graph definition.");
        }
        return GraphFactory(name, Resolve(slots));
    }

    /// <summary>未指定のスロットを <see cref="TemplateSlot.Placeholder"/> で補う。</summary>
    private IReadOnlyDictionary<string, string> Resolve(IReadOnlyDictionary<string, string>? slots)
    {
        var resolved = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var slot in Slots)
        {
            var value = slots is not null && slots.TryGetValue(slot.Key, out var supplied) && !string.IsNullOrWhiteSpace(supplied)
                ? supplied
                : slot.Placeholder;
            resolved[slot.Key] = value;
        }
        return resolved;
    }
}

/// <summary>組み込みテンプレートの一覧 (案E)。</summary>
public static class DefinitionTemplates
{
    public static IReadOnlyList<DefinitionTemplate> All { get; } = BuildAll();

    public static IReadOnlyList<DefinitionTemplate> For(DefinitionKind kind)
        => All.Where(template => template.Kind == kind).ToArray();

    public static DefinitionTemplate? Find(string id)
        => All.FirstOrDefault(template => string.Equals(template.Id, id, StringComparison.Ordinal));

    private static IReadOnlyList<DefinitionTemplate> BuildAll() =>
    [
        ReviewTeam(),
        ResearchWriteTeam(),
        SoloTeam(),
        LinearGraph(),
        ApprovalGraph(),
        QualityLoopGraph(),
        ParallelJoinGraph(),
    ];

    // ---------------------------------------------------------------- team

    private static DefinitionTemplate ReviewTeam() => new()
    {
        Id = "team-review",
        Kind = DefinitionKind.Team,
        Title = "レビュー体制",
        Summary = "統括が作業を割り振り、実装役の成果を検証役が確かめる 3 名構成。",
        WhenToUse = "作ったものを別の目で確かめてから出したいとき。まず選んで間違いが少ない構成。",
        Slots =
        [
            new TemplateSlot { Key = "lead", Label = "統括", Description = "分解と進行管理を担うエージェント。", Placeholder = "orchestrator" },
            new TemplateSlot { Key = "worker", Label = "実装役", Description = "実際に手を動かすエージェント。", Placeholder = "implementer" },
            new TemplateSlot { Key = "checker", Label = "検証役", Description = "成果を確かめて指摘を返すエージェント。", Placeholder = "reviewer" },
        ],
        TeamFactory = (name, slots) => new TeamDefinition
        {
            Name = name,
            DisplayName = name,
            Description = "実装と検証を分けて進めるチーム。",
            Orchestrator = new TeamOrchestrator { Agent = slots["lead"] },
            Members =
            [
                new TeamMember { Agent = slots["worker"], Role = "実装", Scope = "成果物の作成" },
                new TeamMember { Agent = slots["checker"], Role = "検証", Scope = "成果物の確認と指摘" },
            ],
            ChannelsDefault = ChannelDefault.ViaOrchestrator,
            ChannelsAllow =
            [
                new ChannelRule
                {
                    From = slots["checker"],
                    To = slots["worker"],
                    Kinds = [MessageKind.Question, MessageKind.Answer],
                },
            ],
            Limits = new TeamLimits { MaxDelegationDepth = 2, MaxParallelInstances = 3 },
        },
    };

    private static DefinitionTemplate ResearchWriteTeam() => new()
    {
        Id = "team-research-write",
        Kind = DefinitionKind.Team,
        Title = "調査と執筆",
        Summary = "調査役が集めた材料を執筆役がまとめる 3 名構成。調査結果は直接共有できる。",
        WhenToUse = "material を集めてから文章や資料に落とす仕事。調べる人と書く人を分けたいとき。",
        Slots =
        [
            new TemplateSlot { Key = "lead", Label = "統括", Description = "全体の進行を管理するエージェント。", Placeholder = "orchestrator" },
            new TemplateSlot { Key = "researcher", Label = "調査役", Description = "情報を集めて整理するエージェント。", Placeholder = "researcher" },
            new TemplateSlot { Key = "writer", Label = "執筆役", Description = "集まった材料を文章にするエージェント。", Placeholder = "writer" },
        ],
        TeamFactory = (name, slots) => new TeamDefinition
        {
            Name = name,
            DisplayName = name,
            Description = "調査と執筆を分担するチーム。",
            Orchestrator = new TeamOrchestrator { Agent = slots["lead"] },
            Members =
            [
                new TeamMember { Agent = slots["researcher"], Role = "調査", Scope = "情報収集と整理" },
                new TeamMember { Agent = slots["writer"], Role = "執筆", Scope = "文章化" },
            ],
            ChannelsDefault = ChannelDefault.ViaOrchestrator,
            ChannelsAllow =
            [
                new ChannelRule
                {
                    From = slots["researcher"],
                    To = slots["writer"],
                    Kinds = [MessageKind.Share, MessageKind.Question, MessageKind.Answer],
                },
            ],
            Limits = new TeamLimits { MaxDelegationDepth = 2, MaxParallelInstances = 4 },
        },
    };

    private static DefinitionTemplate SoloTeam() => new()
    {
        Id = "team-solo",
        Kind = DefinitionKind.Team,
        Title = "最小構成",
        Summary = "統括と担当 1 名だけ。チーム定義として成立する最小の形。",
        WhenToUse = "まず動かして仕組みを確かめたいとき。ここから members を足していく。",
        Slots =
        [
            new TemplateSlot { Key = "lead", Label = "統括", Description = "受け取った目標を分解するエージェント。", Placeholder = "orchestrator" },
            new TemplateSlot { Key = "worker", Label = "担当", Description = "実作業を行うエージェント。", Placeholder = "worker" },
        ],
        TeamFactory = (name, slots) => new TeamDefinition
        {
            Name = name,
            DisplayName = name,
            Description = "統括と担当 1 名の最小チーム。",
            Orchestrator = new TeamOrchestrator { Agent = slots["lead"] },
            Members = [new TeamMember { Agent = slots["worker"], Role = "担当" }],
            Limits = new TeamLimits { MaxDelegationDepth = 1, MaxParallelInstances = 2 },
        },
    };

    // --------------------------------------------------------------- graph

    private static DefinitionTemplate LinearGraph() => new()
    {
        Id = "graph-linear3",
        Kind = DefinitionKind.Graph,
        Title = "直列 3 ステップ",
        Summary = "計画、実行、まとめを順番に流すだけのグラフ。分岐も並列もない。",
        WhenToUse = "手順が一本道のとき。グラフの書き方を覚える最初の 1 本にも向く。",
        Slots =
        [
            new TemplateSlot { Key = "planner", Label = "計画役", Description = "目標を作業手順に落とすエージェント。", Placeholder = "planner" },
            new TemplateSlot { Key = "worker", Label = "実行役", Description = "手順に沿って作業するエージェント。", Placeholder = "worker" },
            new TemplateSlot { Key = "summarizer", Label = "まとめ役", Description = "結果を報告にまとめるエージェント。", Placeholder = "summarizer" },
        ],
        GraphFactory = (name, slots) => new GraphDefinition
        {
            Name = name,
            DisplayName = name,
            Description = "計画、実行、まとめを順に流す。",
            Nodes =
            [
                Agent("plan", slots["planner"], "${mission.goal} を達成するための手順を作ってください。"),
                Agent("work", slots["worker"], "次の手順に沿って作業してください。\n${nodes.plan.output}"),
                Agent("summarize", slots["summarizer"], "次の作業結果を報告としてまとめてください。\n${nodes.work.output}"),
            ],
            Edges = [Edge("plan", "work"), Edge("work", "summarize")],
            Layout = Layout(("plan", 80, 80), ("work", 380, 80), ("summarize", 680, 80)),
        },
    };

    private static DefinitionTemplate ApprovalGraph() => new()
    {
        Id = "graph-approval",
        Kind = DefinitionKind.Graph,
        Title = "承認つき",
        Summary = "下書きを作り、人の承認を挟んでから確定処理へ進むグラフ。",
        WhenToUse = "外に出るもの、取り消しにくい操作を含むとき。承認で止められる形にしておきたい場合。",
        Slots =
        [
            new TemplateSlot { Key = "drafter", Label = "下書き役", Description = "案を作るエージェント。", Placeholder = "drafter" },
            new TemplateSlot { Key = "publisher", Label = "確定役", Description = "承認後の処理を行うエージェント。", Placeholder = "publisher" },
        ],
        GraphFactory = (name, slots) => new GraphDefinition
        {
            Name = name,
            DisplayName = name,
            Description = "下書き、承認、確定の順に進む。",
            Nodes =
            [
                Agent("draft", slots["drafter"], "${mission.goal} の案を作ってください。"),
                new GraphNode
                {
                    Id = "approve",
                    Kind = NodeKind.Approval,
                    Title = "内容の承認",
                    Summary = "次の内容で進めてよいか確認してください。\n${nodes.draft.output}",
                    TimeoutSeconds = 900,
                },
                Agent("publish", slots["publisher"], "承認された次の内容で確定処理を行ってください。\n${nodes.draft.output}"),
            ],
            Edges = [Edge("draft", "approve"), Edge("approve", "publish")],
            Layout = Layout(("draft", 80, 80), ("approve", 380, 80), ("publish", 680, 80)),
        },
    };

    private static DefinitionTemplate QualityLoopGraph() => new()
    {
        Id = "graph-quality-loop",
        Kind = DefinitionKind.Graph,
        Title = "品質ループ",
        Summary = "下書きを作り、評価者の点数が基準を超えるまで書き直しを繰り返す。",
        WhenToUse = "一発で仕上がらない仕事。回数とスコアの両方で止める形にしてある。",
        Slots =
        [
            new TemplateSlot { Key = "drafter", Label = "下書き役", Description = "最初の案を作るエージェント。", Placeholder = "drafter" },
            new TemplateSlot { Key = "reviser", Label = "改稿役", Description = "指摘を受けて直すエージェント。", Placeholder = "reviser" },
            new TemplateSlot { Key = "judge", Label = "評価役", Description = "出来を採点するエージェント。", Placeholder = "judge" },
        ],
        GraphFactory = (name, slots) => new GraphDefinition
        {
            Name = name,
            DisplayName = name,
            Description = "基準を満たすまで書き直しを繰り返す。",
            Nodes =
            [
                Agent("draft", slots["drafter"], "${mission.goal} の初稿を作ってください。"),
                new GraphNode
                {
                    Id = "improve",
                    Kind = NodeKind.Loop,
                    Body = "improve-body",
                    Stop = new LoopStopCondition { MaxIterations = 5, ScoreThreshold = 0.8 },
                    Evaluator = new NodeEvaluatorSpec { Kind = "agent", Agent = slots["judge"] },
                },
            ],
            Edges = [Edge("draft", "improve")],
            Subgraphs = new Dictionary<string, SubgraphDefinition>(StringComparer.Ordinal)
            {
                ["improve-body"] = new SubgraphDefinition
                {
                    Nodes =
                    [
                        Agent("revise", slots["reviser"],
                            "次の原稿を、直前の指摘を踏まえて改善してください。\n${loop.previous.output}"),
                    ],
                    Edges = [],
                },
            },
            Layout = Layout(("draft", 80, 80), ("improve", 380, 80)),
        },
    };

    private static DefinitionTemplate ParallelJoinGraph() => new()
    {
        Id = "graph-parallel-join",
        Kind = DefinitionKind.Graph,
        Title = "並列と合流",
        Summary = "2 つの調査を同時に走らせ、両方揃ってからまとめる。",
        WhenToUse = "互いに依存しない作業を同時に進めたいとき。片方が失敗したときの扱いも決めてある。",
        Slots =
        [
            new TemplateSlot { Key = "left", Label = "経路 A の担当", Description = "片方の作業を行うエージェント。", Placeholder = "researcher-a" },
            new TemplateSlot { Key = "right", Label = "経路 B の担当", Description = "もう片方の作業を行うエージェント。", Placeholder = "researcher-b" },
            new TemplateSlot { Key = "merger", Label = "まとめ役", Description = "両方の結果を統合するエージェント。", Placeholder = "summarizer" },
        ],
        GraphFactory = (name, slots) => new GraphDefinition
        {
            Name = name,
            DisplayName = name,
            Description = "2 経路を同時に走らせて合流させる。",
            Nodes =
            [
                new GraphNode { Id = "split", Kind = NodeKind.Parallel },
                Agent("track-a", slots["left"], "${mission.goal} について、担当 A の観点で調べてください。"),
                Agent("track-b", slots["right"], "${mission.goal} について、担当 B の観点で調べてください。"),
                new GraphNode
                {
                    Id = "merge",
                    Kind = NodeKind.Join,
                    JoinPolicy = JoinPolicy.All,
                    OnPartialFailure = PartialFailurePolicy.Continue,
                },
                Agent("report", slots["merger"],
                    "次の 2 つの調査結果を統合して報告にまとめてください。\nA: ${nodes.track-a.output}\nB: ${nodes.track-b.output}"),
            ],
            Edges =
            [
                Edge("split", "track-a"),
                Edge("split", "track-b"),
                Edge("track-a", "merge"),
                Edge("track-b", "merge"),
                Edge("merge", "report"),
            ],
            Layout = Layout(
                ("split", 80, 160),
                ("track-a", 380, 60),
                ("track-b", 380, 260),
                ("merge", 680, 160),
                ("report", 980, 160)),
        },
    };

    // --------------------------------------------------------------- 補助

    private static GraphNode Agent(string id, string agent, string input) => new()
    {
        Id = id,
        Kind = NodeKind.Agent,
        Agent = agent,
        Input = input,
    };

    private static GraphEdge Edge(string from, string to) => new()
    {
        Id = $"{from}-to-{to}",
        From = from,
        To = to,
    };

    private static IReadOnlyDictionary<string, (double X, double Y)> Layout(
        params (string Id, double X, double Y)[] positions)
        => positions.ToDictionary(item => item.Id, item => (item.X, item.Y), StringComparer.Ordinal);
}
