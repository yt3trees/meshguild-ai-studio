using System.Text.Json;
using System.Text.Json.Serialization;

namespace WorkAgents.Tray;

/// <summary>
/// ランチャー専用の簡易設定(Web/Hostのポート番号、data-model.md「LauncherSettings」参照)。
/// contracts/tray-settings-file-contract.mdのスキーマ・バリデーションに対応する。
/// </summary>
public sealed class LauncherSettings
{
    public const int DefaultWebPort = 5049;
    public const int DefaultHostPort = 5160;
    public const int MinPort = 1024;
    public const int MaxPort = 65535;

    [JsonPropertyName("webPort")]
    public int WebPort { get; init; } = DefaultWebPort;

    [JsonPropertyName("hostPort")]
    public int HostPort { get; init; } = DefaultHostPort;

    /// <summary>
    /// Workspace保存先フォルダの上書き。null/空文字は「Host/Web自身の既定値(C:\work-agents\runs)を使う」を意味し、
    /// その場合は環境変数を渡さない(<see cref="ProcessSupervisor"/>参照)。
    /// </summary>
    [JsonPropertyName("workspaceRoot")]
    public string? WorkspaceRoot { get; init; }

    /// <summary>Artifacts保存先フォルダの上書き。意味論は<see cref="WorkspaceRoot"/>と同じ。</summary>
    [JsonPropertyName("artifactsRoot")]
    public string? ArtifactsRoot { get; init; }

    /// <summary>
    /// Run/Mission/Approval等の状態を保存するSQLiteファイルの上書き。
    /// null/空文字はHost/Webのappsettings既定値を使うことを意味する。
    /// </summary>
    [JsonPropertyName("databasePath")]
    public string? DatabasePath { get; init; }

    /// <summary>HostのMCP endpointを有効にするか。既定は無効。</summary>
    [JsonPropertyName("mcpEnabled")]
    public bool McpEnabled { get; init; }

    /// <summary>
    /// 標準のエージェント定義(配布時は共通definitions/)に加えて読み込む、追加の定義フォルダ群。
    /// 配列の順序をそのままAgents:DefinitionSourcesの順序として扱い、後のソースを優先する。
    /// </summary>
    [JsonPropertyName("additionalAgentDefinitionPaths")]
    public List<string> AdditionalAgentDefinitionPaths { get; init; } = [];

    /// <summary>
    /// 旧バージョンの単一パス設定をソース互換のために受け入れる。保存時は新しい配列形式へ移行する。
    /// </summary>
    [JsonIgnore]
    public string? AdditionalAgentDefinitionPath { get; init; }

    /// <summary>
    /// 設定画面の専用項目でカバーしていない、その他のappsettingsキーや将来追加されるキーの
    /// 汎用オーバーライド。例: "Custom:Option"。
    /// キーはASP.NET Coreの設定キー記法(コロン区切り)のまま保持し、子プロセスへ渡す際にのみ
    /// 環境変数用の"__"区切りへ変換する(<see cref="ProcessSupervisor"/>参照)。
    /// APIキーなどの機密情報は絶対に入れないこと(Local secret storeを使う、Constitution Principle I)。
    /// </summary>
    [JsonPropertyName("additionalConfiguration")]
    public Dictionary<string, string>? AdditionalConfiguration { get; init; }

    /// <summary>FR-011: 数値として不正、1024〜65535の範囲外、または両ポートが同一値の場合は拒否する。</summary>
    public static bool TryValidate(int webPort, int hostPort, out string? error)
    {
        if (webPort < MinPort || webPort > MaxPort)
        {
            error = $"Webポート番号は{MinPort}〜{MaxPort}の範囲で指定してください。";
            return false;
        }

        if (hostPort < MinPort || hostPort > MaxPort)
        {
            error = $"Hostポート番号は{MinPort}〜{MaxPort}の範囲で指定してください。";
            return false;
        }

        if (webPort == hostPort)
        {
            error = "WebポートとHostポートには異なる番号を指定してください。";
            return false;
        }

        error = null;
        return true;
    }

    /// <summary>
    /// Workspace/Artifacts/追加エージェント定義フォルダ群のバリデーション。未指定(null/空)は
    /// 「Host/Webの既定値を使う」ことを意味し常に有効。指定された場合はパスとして不正な文字のみ拒否する
    /// (存在チェックはしない。フォルダは初回起動時にHost/Web側で作成される想定のため)。
    /// </summary>
    public static bool TryValidatePaths(
        string? workspaceRoot,
        string? artifactsRoot,
        IReadOnlyList<string>? additionalAgentDefinitionPaths,
        out string? error)
    {
        var paths = new List<(string Label, string? Value)>
        {
            ("Workspaceフォルダ", workspaceRoot),
            ("Artifactsフォルダ", artifactsRoot),
        };

        if (additionalAgentDefinitionPaths is not null)
        {
            paths.AddRange(
                additionalAgentDefinitionPaths.Select(
                    (path, index) => ($"追加のエージェント定義フォルダ({index + 1})", (string?)path)));
        }

        foreach (var (label, value) in paths)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            if (value.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
            {
                error = $"{label}に使用できない文字が含まれています。";
                return false;
            }
        }

        error = null;
        return true;
    }

    /// <summary>状態SQLiteファイルのパスを検証する。未指定(null/空)は常に有効。</summary>
    public static bool TryValidateDatabasePath(string? databasePath, out string? error)
    {
        if (!string.IsNullOrWhiteSpace(databasePath) && databasePath.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
        {
            error = "状態データベースのパスに使用できない文字が含まれています。";
            return false;
        }

        error = null;
        return true;
    }

    /// <summary>
    /// 設定ダイアログの詳細設定テキスト欄(1行1エントリ、"Key:SubKey=Value"形式、
    /// "#"始まりはコメント、空行は無視)を解析する。キーが空、または"="を含まない行があれば拒否する。
    /// </summary>
    public static bool TryParseAdditionalConfiguration(string text, out Dictionary<string, string> parsed, out string? error)
    {
        parsed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.Trim().TrimEnd('\r');
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            var separatorIndex = line.IndexOf('=');
            if (separatorIndex <= 0)
            {
                error = $"詳細設定の行「{line}」は「キー=値」形式で入力してください。";
                return false;
            }

            var key = line[..separatorIndex].Trim();
            var value = line[(separatorIndex + 1)..].Trim();
            if (key.Length == 0)
            {
                error = $"詳細設定の行「{line}」のキーが空です。";
                return false;
            }

            parsed[key] = value;
        }

        error = null;
        return true;
    }

    /// <summary>詳細設定テキスト欄への表示用に、辞書を"Key=Value"の複数行テキストへ整形する。</summary>
    public static string FormatAdditionalConfiguration(IReadOnlyDictionary<string, string>? configuration)
    {
        if (configuration is null || configuration.Count == 0)
        {
            return "";
        }

        return string.Join('\n', configuration.Select(pair => $"{pair.Key}={pair.Value}"));
    }

    public static string GetDefaultFilePath()
    {
        var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(root, "WorkAgents", "tray-settings.json");
    }

    /// <summary>
    /// ファイル欠落・JSON不正・バリデーション違反のいずれの場合も既定値へフォールバックする
    /// (contracts/tray-settings-file-contract.md「読み込み時の挙動」)。
    /// </summary>
    public static LauncherSettings Load(string filePath, Action<string>? onFallback = null)
    {
        if (!File.Exists(filePath))
        {
            return new LauncherSettings();
        }

        try
        {
            var json = File.ReadAllText(filePath);
            var persisted = JsonSerializer.Deserialize<PersistedLauncherSettings>(json);
            if (persisted is null)
            {
                onFallback?.Invoke("設定ファイルが空、または解析できませんでした。既定値を使用します。");
                return new LauncherSettings();
            }

            var additionalPaths = persisted.AdditionalAgentDefinitionPaths?
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Select(path => path.Trim())
                .ToList() ?? [];
            if (additionalPaths.Count == 0 && !string.IsNullOrWhiteSpace(persisted.LegacyAdditionalAgentDefinitionPath))
            {
                additionalPaths.Add(persisted.LegacyAdditionalAgentDefinitionPath.Trim());
            }

            var loaded = new LauncherSettings
            {
                WebPort = persisted.WebPort,
                HostPort = persisted.HostPort,
                WorkspaceRoot = persisted.WorkspaceRoot,
                ArtifactsRoot = persisted.ArtifactsRoot,
                DatabasePath = persisted.DatabasePath,
                McpEnabled = persisted.McpEnabled,
                AdditionalAgentDefinitionPaths = additionalPaths,
                AdditionalAgentDefinitionPath = persisted.LegacyAdditionalAgentDefinitionPath,
                AdditionalConfiguration = persisted.AdditionalConfiguration,
            };

            if (!TryValidate(loaded.WebPort, loaded.HostPort, out var portError))
            {
                onFallback?.Invoke($"設定ファイルの内容が不正です({portError})。既定値を使用します。");
                return new LauncherSettings();
            }

            if (!TryValidatePaths(loaded.WorkspaceRoot, loaded.ArtifactsRoot, loaded.AdditionalAgentDefinitionPaths, out var pathError))
            {
                onFallback?.Invoke($"設定ファイルの内容が不正です({pathError})。既定値を使用します。");
                return new LauncherSettings();
            }

            if (!TryValidateDatabasePath(loaded.DatabasePath, out var databasePathError))
            {
                onFallback?.Invoke($"設定ファイルの内容が不正です({databasePathError})。既定値を使用します。");
                return new LauncherSettings();
            }

            return loaded;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            onFallback?.Invoke($"設定ファイルの読み込みに失敗しました({ex.GetType().Name})。既定値を使用します。");
            return new LauncherSettings();
        }
    }

    /// <summary>保存前に必ず<see cref="TryValidate"/>を呼び出すこと(FR-011)。</summary>
    public void Save(string filePath)
    {
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(
            new PersistedLauncherSettings
            {
                WebPort = WebPort,
                HostPort = HostPort,
                WorkspaceRoot = WorkspaceRoot,
                ArtifactsRoot = ArtifactsRoot,
                DatabasePath = DatabasePath,
                McpEnabled = McpEnabled,
                AdditionalAgentDefinitionPaths = GetAdditionalAgentDefinitionPaths().ToList(),
                AdditionalConfiguration = AdditionalConfiguration,
            },
            new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(filePath, json);
    }

    /// <summary>標準ソースの後ろに追加する定義ソースを、空欄を除いて入力順で返す。</summary>
    public IReadOnlyList<string> GetAdditionalAgentDefinitionPaths()
    {
        if (AdditionalAgentDefinitionPaths.Count > 0)
        {
            return AdditionalAgentDefinitionPaths
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Select(path => path.Trim())
                .ToArray();
        }

        return string.IsNullOrWhiteSpace(AdditionalAgentDefinitionPath)
            ? []
            : [AdditionalAgentDefinitionPath.Trim()];
    }

    private sealed class PersistedLauncherSettings
    {
        [JsonPropertyName("webPort")]
        public int WebPort { get; init; } = DefaultWebPort;

        [JsonPropertyName("hostPort")]
        public int HostPort { get; init; } = DefaultHostPort;

        [JsonPropertyName("workspaceRoot")]
        public string? WorkspaceRoot { get; init; }

        [JsonPropertyName("artifactsRoot")]
        public string? ArtifactsRoot { get; init; }

        [JsonPropertyName("databasePath")]
        public string? DatabasePath { get; init; }

        [JsonPropertyName("mcpEnabled")]
        public bool McpEnabled { get; init; }

        [JsonPropertyName("additionalAgentDefinitionPaths")]
        public List<string>? AdditionalAgentDefinitionPaths { get; init; }

        [JsonPropertyName("additionalAgentDefinitionPath")]
        public string? LegacyAdditionalAgentDefinitionPath { get; init; }

        [JsonPropertyName("additionalConfiguration")]
        public Dictionary<string, string>? AdditionalConfiguration { get; init; }
    }
}
