using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace WorkAgents.Agents.Loading;

/// <summary><c>team.yaml</c> の実装言語非依存表現 (contracts/team-yaml.md)。</summary>
public sealed class TeamYaml
{
    public int? Version { get; set; }

    public string? Name { get; set; }

    public string? DisplayName { get; set; }

    public string? Description { get; set; }

    public TeamOrchestratorYaml? Orchestrator { get; set; }

    public List<TeamMemberYaml>? Members { get; set; }

    public TeamChannelsYaml? Channels { get; set; }

    public TeamLimitsYaml? Limits { get; set; }

    public TeamEvaluationYaml? Evaluation { get; set; }
}

public sealed class TeamOrchestratorYaml
{
    public string? Agent { get; set; }

    public int? MaxInstances { get; set; }
}

public sealed class TeamMemberYaml
{
    public string? Agent { get; set; }

    public string? Role { get; set; }

    public string? Scope { get; set; }

    public int? MaxInstances { get; set; }
}

public sealed class TeamChannelsYaml
{
    public string? Default { get; set; }

    public List<TeamChannelAllowYaml>? Allow { get; set; }
}

public sealed class TeamChannelAllowYaml
{
    public string? From { get; set; }

    public string? To { get; set; }

    public List<string>? Kinds { get; set; }
}

public sealed class TeamLimitsYaml
{
    public int? MaxDelegationDepth { get; set; }

    public int? MaxParallelInstances { get; set; }

    public int? NoProgressRoundTrips { get; set; }

    public int? AskTimeoutSeconds { get; set; }
}

public sealed class TeamEvaluationYaml
{
    public string? Evaluator { get; set; }

    public double? ScoreThreshold { get; set; }
}

/// <summary>team.yaml の読み込みに失敗したときの例外。メッセージは contracts/team-yaml.md の文言に一致する。</summary>
public sealed class TeamValidationException : Exception
{
    public TeamValidationException(string message) : base(message)
    {
    }
}

/// <summary>
/// 規約ローダ。<c>team.yaml</c> を YamlDotNet で厳格にデシリアライズする。
/// 未知キーは既定 (IgnoreUnmatchedProperties を付けない) で例外になるため、
/// それを <see cref="TeamValidationException"/> へ変換して「unknown key」規則を実現する。
/// </summary>
internal static class TeamYamlSerializer
{
    private static readonly IDeserializer _deserializer =
        new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .Build();

    public static TeamYaml Deserialize(string yaml)
    {
        try
        {
            return _deserializer.Deserialize<TeamYaml>(yaml) ?? new TeamYaml();
        }
        catch (YamlException ex)
        {
            throw new TeamValidationException($"unknown key: {ex.Message}");
        }
    }
}
