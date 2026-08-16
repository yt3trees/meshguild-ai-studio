using System.Globalization;
using Forms = System.Windows.Forms;
using Microsoft.Win32;
using System.Windows;
using System.Windows.Controls;
using Wpf.Ui.Controls;
using TextBox = Wpf.Ui.Controls.TextBox;
using WpfSaveFileDialog = Microsoft.Win32.SaveFileDialog;

namespace WorkAgents.Tray;

/// <summary>
/// WPF UIで構成した設定画面。トレイ本体のWinFormsとは独立して、DPIとテーマをWPF側で管理する。
/// </summary>
public partial class SettingsWindow : FluentWindow
{
    private const string StandardDefinitionSource = "標準ソース (自動) / 共通definitions";

    private readonly Dictionary<string, string> _configurationForDisplay;
    private readonly Dictionary<string, TextBox> _configurationInputs = new(StringComparer.OrdinalIgnoreCase);

    private static readonly string[] ConfigurableKeys =
    [
        "Profile",
        "Workspace:Retention:Enabled",
        "Workspace:Retention:RetentionPeriod",
        "Workspace:Retention:SweepInterval",
        "Runs:QueueCapacity",
        "SecretStore:Root",
        "GitAuth:AppId",
        "GitAuth:InstallationId",
        "GitAuth:PrivateKeySecretName",
        "Orchestration:HostBaseUrl",
        "Orchestration:Engine:Enabled",
        "Orchestration:Limits:MaxConcurrentMissions",
        "Orchestration:Limits:MaxConcurrentAgents",
        "Orchestration:Limits:AskTimeoutSeconds",
        "Orchestration:Checkpoint:MaxWorkspaceBytes",
        "Orchestration:Triggers:Webhook:Loopback",
        "Streaming:Enabled",
        "Mcp:Enabled",
    ];

    public SettingsWindow(LauncherSettings currentSettings)
    {
        InitializeComponent();

        _configurationForDisplay = PrepareConfigurationForDisplay(
            currentSettings,
            out var workspaceRoot,
            out var artifactsRoot,
            out var databasePath);

        WebPortInput.Text = currentSettings.WebPort.ToString(CultureInfo.InvariantCulture);
        HostPortInput.Text = currentSettings.HostPort.ToString(CultureInfo.InvariantCulture);
        WorkspaceRootInput.Text = workspaceRoot ?? "";
        ArtifactsRootInput.Text = artifactsRoot ?? "";
        DatabasePathInput.Text = databasePath ?? "";

        _configurationInputs["Profile"] = ProfileInput;
        _configurationInputs["Workspace:Retention:Enabled"] = RetentionEnabledInput;
        _configurationInputs["Workspace:Retention:RetentionPeriod"] = RetentionPeriodInput;
        _configurationInputs["Workspace:Retention:SweepInterval"] = SweepIntervalInput;
        _configurationInputs["Runs:QueueCapacity"] = QueueCapacityInput;
        _configurationInputs["SecretStore:Root"] = SecretStoreRootInput;
        _configurationInputs["GitAuth:AppId"] = GitAppIdInput;
        _configurationInputs["GitAuth:InstallationId"] = GitInstallationIdInput;
        _configurationInputs["GitAuth:PrivateKeySecretName"] = PrivateKeySecretNameInput;
        _configurationInputs["Orchestration:HostBaseUrl"] = HostBaseUrlInput;
        _configurationInputs["Orchestration:Engine:Enabled"] = EngineEnabledInput;
        _configurationInputs["Orchestration:Limits:MaxConcurrentMissions"] = MaxConcurrentMissionsInput;
        _configurationInputs["Orchestration:Limits:MaxConcurrentAgents"] = MaxConcurrentAgentsInput;
        _configurationInputs["Orchestration:Limits:AskTimeoutSeconds"] = AskTimeoutSecondsInput;
        _configurationInputs["Orchestration:Checkpoint:MaxWorkspaceBytes"] = MaxWorkspaceBytesInput;
        _configurationInputs["Orchestration:Triggers:Webhook:Loopback"] = WebhookLoopbackInput;
        _configurationInputs["Streaming:Enabled"] = StreamingEnabledInput;
        _configurationInputs["Mcp:Enabled"] = McpEnabledInput;

        foreach (var key in ConfigurableKeys)
        {
            _configurationInputs[key].Text = GetConfigurationValue(_configurationForDisplay, key) ?? "";
        }

        ToolPluginDirectoriesInput.Text = FormatIndexedConfiguration(
            _configurationForDisplay,
            "Agents:ToolPluginDirectories");
        AllowedHostsInput.Text = FormatIndexedConfiguration(
            _configurationForDisplay,
            "Agents:ToolPlugins:AllowedHosts");
        OtherConfigurationInput.Text = LauncherSettings.FormatAdditionalConfiguration(
            GetUnknownConfiguration(_configurationForDisplay));

        DefinitionList.Items.Add(StandardDefinitionSource);
        foreach (var path in currentSettings.GetAdditionalAgentDefinitionPaths())
        {
            DefinitionList.Items.Add(path);
        }

        DefinitionList.SelectedIndex = 0;
        UpdateDefinitionButtons();
    }

    private void BrowseWorkspaceClicked(object sender, RoutedEventArgs e) => BrowseFolder(WorkspaceRootInput);

    private void BrowseArtifactsClicked(object sender, RoutedEventArgs e) => BrowseFolder(ArtifactsRootInput);

    private void BrowseDatabaseClicked(object sender, RoutedEventArgs e)
    {
        var dialog = new WpfSaveFileDialog
        {
            Title = "状態データベースの保存先を選択",
            Filter = "SQLiteデータベース (*.db;*.sqlite)|*.db;*.sqlite|すべてのファイル (*.*)|*.*",
            DefaultExt = "db",
            AddExtension = true,
            OverwritePrompt = false,
            RestoreDirectory = true,
        };

        try
        {
            if (!string.IsNullOrWhiteSpace(DatabasePathInput.Text))
            {
                var directory = Path.GetDirectoryName(DatabasePathInput.Text);
                if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory))
                {
                    dialog.InitialDirectory = directory;
                }

                dialog.FileName = Path.GetFileName(DatabasePathInput.Text);
            }
        }
        catch (ArgumentException)
        {
            dialog.FileName = "work-agents.db";
        }

        if (dialog.ShowDialog(this) == true)
        {
            DatabasePathInput.Text = dialog.FileName;
        }
    }

    private static void BrowseFolder(TextBox target)
    {
        using var dialog = new Forms.FolderBrowserDialog
        {
            Description = "フォルダを選択してください",
            SelectedPath = string.IsNullOrWhiteSpace(target.Text) ? "" : target.Text,
            ShowNewFolderButton = true,
        };

        if (dialog.ShowDialog() == Forms.DialogResult.OK)
        {
            target.Text = dialog.SelectedPath;
        }
    }

    private void AddDefinitionClicked(object sender, RoutedEventArgs e)
    {
        using var dialog = new Forms.FolderBrowserDialog
        {
            Description = "追加するエージェント定義ソースを選択してください",
            ShowNewFolderButton = false,
        };

        if (dialog.ShowDialog() != Forms.DialogResult.OK)
        {
            return;
        }

        if (DefinitionList.Items.Cast<object>()
                .Skip(1)
                .Select(item => item.ToString())
                .Any(path => string.Equals(path, dialog.SelectedPath, StringComparison.OrdinalIgnoreCase)))
        {
            ErrorText.Text = "同じ定義ソースは複数回追加できません。";
            return;
        }

        ErrorText.Text = "";
        DefinitionList.Items.Add(dialog.SelectedPath);
        DefinitionList.SelectedIndex = DefinitionList.Items.Count - 1;
    }

    private void RemoveDefinitionClicked(object sender, RoutedEventArgs e)
    {
        if (DefinitionList.SelectedIndex <= 0)
        {
            return;
        }

        var index = DefinitionList.SelectedIndex;
        DefinitionList.Items.RemoveAt(index);
        DefinitionList.SelectedIndex = Math.Min(index, DefinitionList.Items.Count - 1);
    }

    private void MoveDefinitionUpClicked(object sender, RoutedEventArgs e) => MoveDefinition(-1);

    private void MoveDefinitionDownClicked(object sender, RoutedEventArgs e) => MoveDefinition(1);

    private void MoveDefinition(int offset)
    {
        var from = DefinitionList.SelectedIndex;
        var to = from + offset;
        if (from <= 0 || to <= 0 || to >= DefinitionList.Items.Count)
        {
            return;
        }

        var item = DefinitionList.Items[from];
        DefinitionList.Items.RemoveAt(from);
        DefinitionList.Items.Insert(to, item);
        DefinitionList.SelectedIndex = to;
    }

    private void DefinitionListSelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateDefinitionButtons();

    private void UpdateDefinitionButtons()
    {
        var index = DefinitionList.SelectedIndex;
        var count = DefinitionList.Items.Count;
        RemoveDefinitionButton.IsEnabled = index > 0;
        MoveDefinitionUpButton.IsEnabled = index > 1;
        MoveDefinitionDownButton.IsEnabled = index > 0 && index < count - 1;
    }

    private void SaveClicked(object sender, RoutedEventArgs e)
    {
        ErrorText.Text = "";
        if (!int.TryParse(WebPortInput.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var webPort)
            || !int.TryParse(HostPortInput.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var hostPort))
        {
            ErrorText.Text = "WebポートとHostポートには数値を入力してください。";
            return;
        }

        var workspaceRoot = NullIfBlank(WorkspaceRootInput.Text);
        var artifactsRoot = NullIfBlank(ArtifactsRootInput.Text);
        var databasePath = NullIfBlank(DatabasePathInput.Text);
        var additionalPaths = DefinitionList.Items
            .Cast<object>()
            .Skip(1)
            .Select(item => item.ToString()?.Trim())
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Cast<string>()
            .ToList();

        if (!LauncherSettings.TryValidate(webPort, hostPort, out var portError))
        {
            ErrorText.Text = portError;
            return;
        }

        if (!LauncherSettings.TryParseAdditionalConfiguration(
                OtherConfigurationInput.Text,
                out var additionalConfiguration,
                out var configError))
        {
            ErrorText.Text = configError;
            return;
        }

        var configuredWorkspaceRoot = TakeConfigurationValue(additionalConfiguration, "Workspace:Root");
        var configuredArtifactsRoot = TakeConfigurationValue(additionalConfiguration, "Artifacts:Root");
        var configuredDatabasePath = TakeConfigurationValue(additionalConfiguration, "Runs:DatabasePath");
        workspaceRoot ??= configuredWorkspaceRoot;
        artifactsRoot ??= configuredArtifactsRoot;
        databasePath ??= configuredDatabasePath;

        if (!LauncherSettings.TryValidatePaths(workspaceRoot, artifactsRoot, additionalPaths, out var pathError))
        {
            ErrorText.Text = pathError;
            return;
        }

        if (!LauncherSettings.TryValidateDatabasePath(databasePath, out var databasePathError))
        {
            ErrorText.Text = databasePathError;
            return;
        }

        foreach (var (key, input) in _configurationInputs)
        {
            var value = NullIfBlank(input.Text);
            if (value is null)
            {
                additionalConfiguration.Remove(key);
            }
            else
            {
                additionalConfiguration[key] = value;
            }
        }

        var mcpEnabledText = TakeConfigurationValue(additionalConfiguration, "Mcp:Enabled");
        if (!string.IsNullOrWhiteSpace(mcpEnabledText) && !bool.TryParse(mcpEnabledText, out _))
        {
            ErrorText.Text = "Mcp:Enabledにはtrueまたはfalseを入力してください。";
            return;
        }
        var mcpEnabled = bool.TryParse(mcpEnabledText, out var parsedMcpEnabled) && parsedMcpEnabled;

        ApplyIndexedConfiguration(
            additionalConfiguration,
            "Agents:ToolPluginDirectories",
            ToolPluginDirectoriesInput.Text);
        ApplyIndexedConfiguration(
            additionalConfiguration,
            "Agents:ToolPlugins:AllowedHosts",
            AllowedHostsInput.Text);

        var settings = new LauncherSettings
        {
            WebPort = webPort,
            HostPort = hostPort,
            WorkspaceRoot = workspaceRoot,
            ArtifactsRoot = artifactsRoot,
            DatabasePath = databasePath,
            McpEnabled = mcpEnabled,
            AdditionalAgentDefinitionPaths = additionalPaths,
            AdditionalConfiguration = additionalConfiguration.Count == 0 ? null : additionalConfiguration,
        };

        try
        {
            settings.Save(LauncherSettings.GetDefaultFilePath());
        }
        catch (IOException)
        {
            ErrorText.Text = "設定ファイルを書き込めませんでした。アクセス権を確認してください。";
            return;
        }
        catch (UnauthorizedAccessException)
        {
            ErrorText.Text = "設定ファイルへのアクセスが拒否されました。";
            return;
        }

        System.Windows.MessageBox.Show(
            this,
            "設定を保存しました。変更内容を反映するにはWorkAgentsを再起動してください。",
            "WorkAgents",
            System.Windows.MessageBoxButton.OK,
            System.Windows.MessageBoxImage.Information);
        DialogResult = true;
    }

    private static void ApplyIndexedConfiguration(
        Dictionary<string, string> configuration,
        string prefix,
        string text)
    {
        RemoveConfigurationFamily(configuration, prefix);
        var index = 0;
        foreach (var value in text
                     .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                     .Select(line => line.Trim())
                     .Where(line => line.Length > 0))
        {
            configuration[$"{prefix}:{index++}"] = value;
        }
    }

    private static string? NullIfBlank(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? GetConfigurationValue(IReadOnlyDictionary<string, string> configuration, string key) =>
        configuration.TryGetValue(key, out var value) ? value : null;

    private static string FormatIndexedConfiguration(IReadOnlyDictionary<string, string> configuration, string prefix)
    {
        var indexedPrefix = prefix + ":";
        return string.Join(
            Environment.NewLine,
            configuration
                .Where(pair => pair.Key.StartsWith(indexedPrefix, StringComparison.OrdinalIgnoreCase))
                .OrderBy(pair => ParseConfigurationIndex(pair.Key[indexedPrefix.Length..]))
                .ThenBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                .Select(pair => pair.Value));
    }

    private static int ParseConfigurationIndex(string value) =>
        int.TryParse(value, out var index) ? index : int.MaxValue;

    private static Dictionary<string, string> GetUnknownConfiguration(IReadOnlyDictionary<string, string> configuration)
    {
        var unknown = new Dictionary<string, string>(configuration, StringComparer.OrdinalIgnoreCase);
        foreach (var key in ConfigurableKeys)
        {
            unknown.Remove(key);
        }

        unknown.Remove("Workspace:Root");
        unknown.Remove("Artifacts:Root");
        unknown.Remove("Runs:DatabasePath");
        RemoveConfigurationFamily(unknown, "Agents:ToolPluginDirectories");
        RemoveConfigurationFamily(unknown, "Agents:ToolPlugins:AllowedHosts");
        return unknown;
    }

    private static void RemoveConfigurationFamily(Dictionary<string, string> configuration, string prefix)
    {
        foreach (var key in configuration.Keys
                     .Where(key => key.Equals(prefix, StringComparison.OrdinalIgnoreCase)
                                   || key.StartsWith(prefix + ":", StringComparison.OrdinalIgnoreCase))
                     .ToArray())
        {
            configuration.Remove(key);
        }
    }

    private static Dictionary<string, string> PrepareConfigurationForDisplay(
        LauncherSettings currentSettings,
        out string? workspaceRoot,
        out string? artifactsRoot,
        out string? databasePath)
    {
        var configuration = new Dictionary<string, string>(
            currentSettings.AdditionalConfiguration ?? new Dictionary<string, string>(),
            StringComparer.OrdinalIgnoreCase);
        var configuredWorkspaceRoot = TakeConfigurationValue(configuration, "Workspace:Root");
        var configuredArtifactsRoot = TakeConfigurationValue(configuration, "Artifacts:Root");
        var configuredDatabasePath = TakeConfigurationValue(configuration, "Runs:DatabasePath");
        workspaceRoot = currentSettings.WorkspaceRoot ?? configuredWorkspaceRoot;
        artifactsRoot = currentSettings.ArtifactsRoot ?? configuredArtifactsRoot;
        databasePath = currentSettings.DatabasePath ?? configuredDatabasePath;
        if (!configuration.ContainsKey("Mcp:Enabled"))
        {
            configuration["Mcp:Enabled"] = currentSettings.McpEnabled.ToString().ToLowerInvariant();
        }
        return configuration;
    }

    private static string? TakeConfigurationValue(Dictionary<string, string> configuration, string key)
    {
        var entry = configuration.FirstOrDefault(pair =>
            pair.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
        if (entry.Key is null)
        {
            return null;
        }

        configuration.Remove(entry.Key);
        return entry.Value;
    }
}
