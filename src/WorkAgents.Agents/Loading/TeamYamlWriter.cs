using System.Globalization;
using System.Text;
using WorkAgents.Core.Missions;
using WorkAgents.Core.Teams;

namespace WorkAgents.Agents.Loading;

/// <summary>
/// 編集された <see cref="TeamDefinition"/> を team.yaml へ書き戻す。
/// 既定値と同じ値は出力しない。読み手が「わざわざ書いてある項目 = 意図して変えた項目」と
/// 読めるようにするため。
/// なお YAML のコメントは保持されない。
/// </summary>
public sealed class TeamYamlWriter
{
    private static readonly TeamLimits DefaultLimits = new();

    public Task WriteAsync(TeamDefinition team, string path, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(team);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }
        return File.WriteAllTextAsync(path, ToYaml(team), Encoding.UTF8, ct);
    }

    public string ToYaml(TeamDefinition team)
    {
        ArgumentNullException.ThrowIfNull(team);
        var builder = new StringBuilder();
        builder.AppendLine($"version: {team.Version}");
        builder.AppendLine($"name: {Quote(team.Name)}");
        if (!string.IsNullOrWhiteSpace(team.DisplayName)) builder.AppendLine($"displayName: {Quote(team.DisplayName!)}");
        if (!string.IsNullOrWhiteSpace(team.Description)) builder.AppendLine($"description: {Quote(team.Description!)}");

        builder.AppendLine("orchestrator:");
        builder.AppendLine($"  agent: {Quote(team.Orchestrator.Agent)}");
        if (team.Orchestrator.MaxInstances != 1)
        {
            builder.AppendLine($"  maxInstances: {team.Orchestrator.MaxInstances.ToString(CultureInfo.InvariantCulture)}");
        }

        builder.AppendLine("members:");
        foreach (var member in team.Members)
        {
            builder.AppendLine($"  - agent: {Quote(member.Agent)}");
            if (!string.IsNullOrWhiteSpace(member.Role)) builder.AppendLine($"    role: {Quote(member.Role!)}");
            if (!string.IsNullOrWhiteSpace(member.Scope)) builder.AppendLine($"    scope: {Quote(member.Scope!)}");
            if (member.MaxInstances != 1) builder.AppendLine($"    maxInstances: {member.MaxInstances.ToString(CultureInfo.InvariantCulture)}");
        }

        var writeChannelDefault = team.ChannelsDefault != ChannelDefault.ViaOrchestrator;
        if (writeChannelDefault || team.ChannelsAllow.Count > 0)
        {
            builder.AppendLine("channels:");
            if (writeChannelDefault)
            {
                builder.AppendLine($"  default: {Quote(ToYamlValue(team.ChannelsDefault))}");
            }
            if (team.ChannelsAllow.Count > 0)
            {
                builder.AppendLine("  allow:");
                foreach (var rule in team.ChannelsAllow)
                {
                    builder.AppendLine($"    - from: {Quote(rule.From)}");
                    builder.AppendLine($"      to: {Quote(rule.To)}");
                    if (rule.Kinds.Count > 0)
                    {
                        builder.AppendLine($"      kinds: [{string.Join(", ", rule.Kinds.Select(kind => Quote(ToYamlValue(kind))))}]");
                    }
                }
            }
        }

        var limits = new List<string>();
        if (team.Limits.MaxDelegationDepth != DefaultLimits.MaxDelegationDepth) limits.Add($"  maxDelegationDepth: {team.Limits.MaxDelegationDepth}");
        if (team.Limits.MaxParallelInstances != DefaultLimits.MaxParallelInstances) limits.Add($"  maxParallelInstances: {team.Limits.MaxParallelInstances}");
        if (team.Limits.NoProgressRoundTrips != DefaultLimits.NoProgressRoundTrips) limits.Add($"  noProgressRoundTrips: {team.Limits.NoProgressRoundTrips}");
        if (team.Limits.AskTimeoutSeconds != DefaultLimits.AskTimeoutSeconds) limits.Add($"  askTimeoutSeconds: {team.Limits.AskTimeoutSeconds}");
        if (limits.Count > 0)
        {
            builder.AppendLine("limits:");
            foreach (var line in limits)
            {
                builder.AppendLine(line);
            }
        }

        if (team.Evaluation is not null &&
            (!string.IsNullOrWhiteSpace(team.Evaluation.Evaluator) || team.Evaluation.ScoreThreshold.HasValue))
        {
            builder.AppendLine("evaluation:");
            if (!string.IsNullOrWhiteSpace(team.Evaluation.Evaluator)) builder.AppendLine($"  evaluator: {Quote(team.Evaluation.Evaluator!)}");
            if (team.Evaluation.ScoreThreshold is { } threshold) builder.AppendLine($"  scoreThreshold: {threshold.ToString("R", CultureInfo.InvariantCulture)}");
        }

        return builder.ToString();
    }

    /// <summary>C# の PascalCase を team.yaml の表記へ戻す。</summary>
    private static string ToYamlValue(ChannelDefault value) => value switch
    {
        ChannelDefault.Direct => "direct",
        _ => "via-orchestrator",
    };

    private static string ToYamlValue(MessageKind kind)
    {
        var text = kind.ToString();
        return char.ToLowerInvariant(text[0]) + text[1..];
    }

    private static string Quote(string value) => GraphYamlWriter.Quote(value);
}
