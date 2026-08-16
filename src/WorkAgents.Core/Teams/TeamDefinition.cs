using WorkAgents.Core.Missions;

namespace WorkAgents.Core.Teams;

/// <summary>直接会話の既定方針 (channels.default)。</summary>
public enum ChannelDefault
{
    /// <summary>すべての会話を統括エージェント経由にする(既定)。</summary>
    ViaOrchestrator,

    /// <summary>チーム参加者同士の直接会話を既定で許可する。</summary>
    Direct,
}

/// <summary>統括エージェント (orchestrator) の定義。</summary>
public sealed record TeamOrchestrator
{
    public required string Agent { get; init; }

    public int MaxInstances { get; init; } = 1;
}

/// <summary>チームのサブエージェント 1 名の定義 (members[])。</summary>
public sealed record TeamMember
{
    public required string Agent { get; init; }

    public string? Role { get; init; }

    public string? Scope { get; init; }

    public int MaxInstances { get; init; } = 1;
}

/// <summary>直接会話を許可する組み合わせ (channels.allow[])。</summary>
public sealed record ChannelRule
{
    public required string From { get; init; }

    public required string To { get; init; }

    public required IReadOnlyList<MessageKind> Kinds { get; init; }
}

/// <summary>チームのガードレール上限 (limits)。</summary>
public sealed record TeamLimits
{
    public int MaxDelegationDepth { get; init; } = 3;

    public int MaxParallelInstances { get; init; } = 6;

    public int NoProgressRoundTrips { get; init; } = 5;

    public int AskTimeoutSeconds { get; init; } = 300;
}

/// <summary>ループの既定評価設定 (evaluation)。</summary>
public sealed record TeamEvaluationDefaults
{
    public string? Evaluator { get; init; }

    public double? ScoreThreshold { get; init; }
}

/// <summary>チーム定義 (contracts/team-yaml.md)。真実の源は team.yaml であり、これはその実装言語表現。</summary>
public sealed record TeamDefinition
{
    public int Version { get; init; } = 1;

    public required string Name { get; init; }

    public string? DisplayName { get; init; }

    public string? Description { get; init; }

    public required TeamOrchestrator Orchestrator { get; init; }

    public required IReadOnlyList<TeamMember> Members { get; init; }

    public ChannelDefault ChannelsDefault { get; init; } = ChannelDefault.ViaOrchestrator;

    public IReadOnlyList<ChannelRule> ChannelsAllow { get; init; } = Array.Empty<ChannelRule>();

    public TeamLimits Limits { get; init; } = new();

    public TeamEvaluationDefaults? Evaluation { get; init; }

    /// <summary>読み込み元フォルダー(実行時参照用)。</summary>
    public string FolderPath { get; init; } = string.Empty;

    /// <summary>この定義を採用した定義ソースの <c>Label</c>(data-model.md「解決済み定義」)。</summary>
    public string SourceLabel { get; init; } = "standard";

    /// <summary>同名で存在したが上書きされた側の <c>Label</c>(0件の場合は衝突なし)。</summary>
    public IReadOnlyList<string> OverriddenSourceLabels { get; init; } = Array.Empty<string>();
}
