using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace WorkAgents.Agents.Loading;

/// <summary><c>agent.yaml</c> の実装言語非依存表現。型は緩く保ち、未知キーは無視する。</summary>
public sealed class AgentYaml
{
    /// <summary>エージェント種別(参考用。MAF 本体の declarative kind とは直結しない)。</summary>
    public string? Kind { get; set; }

    public string? Name { get; set; }

    public string? DisplayName { get; set; }

    public string? Description { get; set; }

    /// <summary>このエージェントに公開する共有 skill 名。未指定時は共有 skill を公開しない。</summary>
    public List<string>? Skills { get; set; }

    /// <summary>Harness 設定(M2〜拘束+denylist)。M1 では使用しない。</summary>
    public AgentHarnessYaml? Harness { get; set; }
}

public sealed class AgentHarnessYaml
{
    public bool Shell { get; set; }

    /// <summary>"workspace" / "artifacts" / null。M2 で実装。</summary>
    public string? FileStore { get; set; }
}

/// <summary>
/// 規約ローダ。<c>agent.yaml</c> を YamlDotNet で缓くデシリアライズする。
/// </summary>
internal static class AgentYamlSerializer
{
    private static readonly IDeserializer _deserializer =
        new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();

    public static AgentYaml Deserialize(string yaml) => _deserializer.Deserialize<AgentYaml>(yaml) ?? new();
}