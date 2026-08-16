using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace WorkAgents.Agents.Tools;

/// <summary>スクリプトツールの実行ランタイム。</summary>
public enum ScriptToolRuntime
{
    Node,
    Python,
}

/// <summary>
/// JavaScript/Pythonで書かれたチーム固有ツール1件分の契約宣言(data-model.md「スクリプトツールマニフェスト」、
/// contracts/script-tool-contract.md)。スクリプト本体と同じフォルダに置かれる <c>&lt;name&gt;.tool.yaml</c> の
/// 実装言語非依存表現。
/// </summary>
public sealed class ScriptToolManifest
{
    public required string Name { get; init; }

    public required string Description { get; init; }

    public required string AgentName { get; init; }

    public required ScriptToolRuntime Runtime { get; init; }

    /// <summary>マニフェストと同じフォルダ内の、実行するスクリプトファイル名。</summary>
    public required string EntryPoint { get; init; }

    /// <summary>"automatic" または "required"(既存 <see cref="AgentToolRegistration"/> と同じ語彙)。</summary>
    public required string Approval { get; init; }

    public int TimeoutSeconds { get; init; } = 30;

    /// <summary>JSON Schemaサブセットで表現される引数スキーマ。未指定時は引数なし。</summary>
    public IReadOnlyDictionary<string, object?> Parameters { get; init; } = new Dictionary<string, object?>();

    /// <summary>このツールが到達する社内/外部ホスト。グローバルallowlistとの突合に使う(FR-013)。</summary>
    public IReadOnlyList<string> AllowedHosts { get; init; } = [];
}

public sealed class ScriptToolManifestValidationException : Exception
{
    public ScriptToolManifestValidationException(string message) : base(message)
    {
    }
}

/// <summary>
/// <c>&lt;name&gt;.tool.yaml</c> の生YAML表現(未検証)。<see cref="ScriptToolManifestSerializer"/> が
/// これを検証して <see cref="ScriptToolManifest"/> へ変換する。
/// </summary>
internal sealed class ScriptToolManifestYaml
{
    public string? Name { get; set; }

    public string? Description { get; set; }

    public string? AgentName { get; set; }

    public string? Runtime { get; set; }

    public string? EntryPoint { get; set; }

    public string? Approval { get; set; }

    public int? TimeoutSeconds { get; set; }

    public Dictionary<string, object?>? Parameters { get; set; }

    public List<string>? AllowedHosts { get; set; }
}

/// <summary>
/// 規約ローダ。<c>&lt;name&gt;.tool.yaml</c> を YamlDotNet で厳格にデシリアライズする(未知キーは例外)。
/// マニフェストは承認要否・到達可能ホストを宣言する安全上重要な契約のため、team.yaml/graph.yaml と同様に
/// 寛容な IgnoreUnmatchedProperties は使わない。
/// </summary>
public static class ScriptToolManifestSerializer
{
    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .Build();

    public static ScriptToolManifest Deserialize(string yaml, string manifestPath)
    {
        ScriptToolManifestYaml raw;
        try
        {
            raw = Deserializer.Deserialize<ScriptToolManifestYaml>(yaml) ?? new ScriptToolManifestYaml();
        }
        catch (YamlException ex)
        {
            throw new ScriptToolManifestValidationException($"unknown key or malformed YAML in '{manifestPath}': {ex.Message}");
        }

        return Validate(raw, manifestPath);
    }

    private static ScriptToolManifest Validate(ScriptToolManifestYaml raw, string manifestPath)
    {
        if (string.IsNullOrWhiteSpace(raw.Name))
        {
            throw new ScriptToolManifestValidationException($"'name' is required in '{manifestPath}'");
        }

        if (string.IsNullOrWhiteSpace(raw.Description))
        {
            throw new ScriptToolManifestValidationException($"'description' is required in '{manifestPath}'");
        }

        if (string.IsNullOrWhiteSpace(raw.AgentName))
        {
            throw new ScriptToolManifestValidationException($"'agentName' is required in '{manifestPath}'");
        }

        if (!Enum.TryParse<ScriptToolRuntime>(raw.Runtime, ignoreCase: true, out var runtime))
        {
            throw new ScriptToolManifestValidationException(
                $"'runtime' must be 'node' or 'python' in '{manifestPath}' (was '{raw.Runtime}')");
        }

        if (string.IsNullOrWhiteSpace(raw.EntryPoint))
        {
            throw new ScriptToolManifestValidationException($"'entryPoint' is required in '{manifestPath}'");
        }

        if (!string.Equals(raw.Approval, "automatic", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(raw.Approval, "required", StringComparison.OrdinalIgnoreCase))
        {
            throw new ScriptToolManifestValidationException(
                $"'approval' must be 'automatic' or 'required' in '{manifestPath}' (was '{raw.Approval}')");
        }

        var timeoutSeconds = raw.TimeoutSeconds ?? 30;
        if (timeoutSeconds <= 0)
        {
            throw new ScriptToolManifestValidationException($"'timeoutSeconds' must be positive in '{manifestPath}'");
        }

        var parameters = new Dictionary<string, object?>();
        if (raw.Parameters is not null)
        {
            foreach (var (key, value) in raw.Parameters)
            {
                parameters[key] = NormalizeYamlValue(value);
            }
        }

        return new ScriptToolManifest
        {
            Name = raw.Name.Trim(),
            Description = raw.Description.Trim(),
            AgentName = raw.AgentName.Trim(),
            Runtime = runtime,
            EntryPoint = raw.EntryPoint.Trim(),
            Approval = raw.Approval!.Trim().ToLowerInvariant(),
            TimeoutSeconds = timeoutSeconds,
            Parameters = parameters,
            AllowedHosts = raw.AllowedHosts ?? [],
        };
    }

    /// <summary>
    /// YamlDotNet が <c>object</c> 型フィールドへ動的デシリアライズした値(ネストしたマッピングは
    /// <c>Dictionary&lt;object, object&gt;</c> になる)を、<c>System.Text.Json</c> でシリアライズ可能な
    /// <c>Dictionary&lt;string, object?&gt;</c>/<c>List&lt;object?&gt;</c>/プリミティブへ正規化する。
    /// </summary>
    private static object? NormalizeYamlValue(object? value)
    {
        switch (value)
        {
            case IDictionary<object, object> map:
                var dict = new Dictionary<string, object?>();
                foreach (var pair in map)
                {
                    dict[pair.Key?.ToString() ?? string.Empty] = NormalizeYamlValue(pair.Value);
                }
                return dict;
            case IList<object> list:
                return list.Select(NormalizeYamlValue).ToList();
            default:
                return value;
        }
    }
}
