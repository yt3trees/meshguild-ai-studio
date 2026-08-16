using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;
using WorkAgents.Core;

namespace WorkAgents.Agents.Loading;

/// <summary><c>workflow.yaml</c> の緩いデシリアライズ表現。未知キーは無視する。</summary>
public sealed class WorkflowYaml
{
    public string? Kind { get; set; }
    public string? Name { get; set; }
    public string? DisplayName { get; set; }
    public string? Description { get; set; }

    public WorkflowScheduleYaml? Schedule { get; set; }

    public List<WorkflowStepYaml> Steps { get; set; } = new();
}

public sealed class WorkflowScheduleYaml
{
    public string? Cron { get; set; }
}

public sealed class WorkflowStepYaml
{
    /// <summary>agent / code / approve / (将来: condition / http / parallel)。未指定時は agent。</summary>
    public string? Kind { get; set; }

    public string? Name { get; set; }
    public string? Agent { get; set; }
    public string? Input { get; set; }

    /// <summary>kind: code の C# スクリプト本文。</summary>
    public string? Code { get; set; }

    /// <summary>kind: code で Code の代わりに外部ファイルを参照する(workflow フォルダからの相対パス)。</summary>
    public string? CodeFile { get; set; }

    /// <summary>kind: approve の承認要求タイトル。</summary>
    public string? Title { get; set; }

    /// <summary>kind: approve の承認要求要約。</summary>
    public string? Summary { get; set; }

    /// <summary>kind: approve のタイムアウト(分)。未指定時は既定。</summary>
    public double? TimeoutMinutes { get; set; }
}

public static class WorkflowYamlSerializer
{
    private static readonly IDeserializer _deserializer =
        new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();

    public static WorkflowYaml Deserialize(string yaml) => _deserializer.Deserialize<WorkflowYaml>(yaml) ?? new();

    public static WorkflowStepKind ParseKind(string? kind)
    {
        return kind?.Trim().ToLowerInvariant() switch
        {
            null or "" or "agent" => WorkflowStepKind.Agent,
            "code" => WorkflowStepKind.Code,
            "approve" => WorkflowStepKind.Approve,
            _ => throw new InvalidOperationException($"unknown workflow step kind: '{kind}'"),
        };
    }
}