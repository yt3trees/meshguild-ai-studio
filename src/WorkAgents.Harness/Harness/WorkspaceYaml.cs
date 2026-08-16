using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace WorkAgents.Harness.Harness;

/// <summary>
/// <c>workspace.yaml</c> の緩い表現(5.2, 5.4)。未設定項目は <see cref="HarnessAgentFactory"/> が
/// <c>ProfileOptions</c> と既定 denylist で補完する。未知キーは無視する(YamlDotNet IgnoreUnmatchedProperties)。
/// </summary>
public sealed class WorkspaceYaml
{
    public WorkspaceFileStoreYaml? FileStore { get; set; }

    public WorkspaceShellYaml? Shell { get; set; }
}

public sealed class WorkspaceFileStoreYaml
{
    /// <summary>"workspace" / "artifacts"。実質的には <c>"workspace"</c> のみシェル作業に使う。</summary>
    public string? Kind { get; set; }

    /// <summary>FileStore 親ルート。未設定時は ProfileOptions.WorkspaceRoot。</summary>
    public string? Root { get; set; }
}

public sealed class WorkspaceShellYaml
{
    public bool? ConfineWorkingDirectory { get; set; }

    /// <summary>拒否コマンドの正規表現リスト。既定 <see cref="ShellPolicyFactory"/> の POSIX+Win 系を(合成で)補完。</summary>
    public List<string>? DenyList { get; set; }

    public List<string>? AllowList { get; set; }

    public int? TimeoutSeconds { get; set; }

    public int? MaxOutputBytes { get; set; }

    /// <summary>"Stateless" / "Persistent"。既定は SDK 任せ(未指定)。</summary>
    public string? Mode { get; set; }
}

internal static class WorkspaceYamlSerializer
{
    private static readonly IDeserializer _deserializer =
        new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();

    public static WorkspaceYaml Deserialize(string yaml) => _deserializer.Deserialize<WorkspaceYaml>(yaml) ?? new();
}