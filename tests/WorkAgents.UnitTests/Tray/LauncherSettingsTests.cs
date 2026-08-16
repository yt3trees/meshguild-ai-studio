using WorkAgents.Tray;

namespace WorkAgents.UnitTests.Tray;

public class LauncherSettingsTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), "WorkAgentsTrayTests_" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    [Theory]
    [InlineData(1023, 5160)] // 範囲外(下限未満)
    [InlineData(65536, 5160)] // 範囲外(上限超過)
    [InlineData(5049, 1023)]
    [InlineData(5049, 65536)]
    [InlineData(5049, 5049)] // 同一値
    public void TryValidate_InvalidInput_ReturnsFalse(int webPort, int hostPort)
    {
        var valid = LauncherSettings.TryValidate(webPort, hostPort, out var error);
        Assert.False(valid);
        Assert.NotNull(error);
    }

    [Theory]
    [InlineData(1024, 65535)]
    [InlineData(5049, 5160)]
    [InlineData(65535, 1024)]
    public void TryValidate_ValidInput_ReturnsTrue(int webPort, int hostPort)
    {
        var valid = LauncherSettings.TryValidate(webPort, hostPort, out var error);
        Assert.True(valid);
        Assert.Null(error);
    }

    [Fact]
    public void Load_FileMissing_ReturnsDefaults()
    {
        var path = Path.Combine(_tempDir, "tray-settings.json");
        var settings = LauncherSettings.Load(path);
        Assert.Equal(LauncherSettings.DefaultWebPort, settings.WebPort);
        Assert.Equal(LauncherSettings.DefaultHostPort, settings.HostPort);
    }

    [Fact]
    public void Load_CorruptJson_FallsBackToDefaultsAndReportsWarning()
    {
        Directory.CreateDirectory(_tempDir);
        var path = Path.Combine(_tempDir, "tray-settings.json");
        File.WriteAllText(path, "{ not valid json");

        string? warning = null;
        var settings = LauncherSettings.Load(path, msg => warning = msg);

        Assert.Equal(LauncherSettings.DefaultWebPort, settings.WebPort);
        Assert.NotNull(warning);
    }

    [Fact]
    public void Load_ValidationViolatingValues_FallsBackToDefaults()
    {
        Directory.CreateDirectory(_tempDir);
        var path = Path.Combine(_tempDir, "tray-settings.json");
        File.WriteAllText(path, "{\"webPort\": 80, \"hostPort\": 5160}");

        var settings = LauncherSettings.Load(path);

        Assert.Equal(LauncherSettings.DefaultWebPort, settings.WebPort);
        Assert.Equal(LauncherSettings.DefaultHostPort, settings.HostPort);
    }

    [Fact]
    public void SaveThenLoad_RoundTrips()
    {
        var path = Path.Combine(_tempDir, "nested", "tray-settings.json");
        var settings = new LauncherSettings { WebPort = 5050, HostPort = 5161 };

        settings.Save(path);
        var loaded = LauncherSettings.Load(path);

        Assert.Equal(5050, loaded.WebPort);
        Assert.Equal(5161, loaded.HostPort);
    }

    [Fact]
    public void SaveThenLoad_RoundTripsOptionalPaths()
    {
        var path = Path.Combine(_tempDir, "tray-settings.json");
        var settings = new LauncherSettings
        {
            WorkspaceRoot = @"D:\custom\workspace",
            ArtifactsRoot = @"D:\custom\artifacts",
            DatabasePath = @"D:\custom\state\work-agents.db",
            AdditionalAgentDefinitionPaths =
            [
                @"D:\custom\team-agents",
                @"D:\custom\shared-agents",
            ],
        };

        settings.Save(path);
        var loaded = LauncherSettings.Load(path);

        Assert.Equal(@"D:\custom\workspace", loaded.WorkspaceRoot);
        Assert.Equal(@"D:\custom\artifacts", loaded.ArtifactsRoot);
        Assert.Equal(@"D:\custom\state\work-agents.db", loaded.DatabasePath);
        Assert.Equal(
            [@"D:\custom\team-agents", @"D:\custom\shared-agents"],
            loaded.AdditionalAgentDefinitionPaths);
    }

    [Fact]
    public void SaveThenLoad_RoundTripsMcpEnabled()
    {
        var path = Path.Combine(_tempDir, "tray-settings.json");
        var settings = new LauncherSettings { McpEnabled = true };

        settings.Save(path);
        var loaded = LauncherSettings.Load(path);

        Assert.True(loaded.McpEnabled);
    }

    [Theory]
    [InlineData(null, null, null)]
    [InlineData("", "", "")]
    [InlineData(@"C:\work-agents\runs", null, null)]
    public void TryValidatePaths_BlankOrValidValues_ReturnsTrue(string? workspaceRoot, string? artifactsRoot, string? additionalPath)
    {
        var additionalPaths = additionalPath is null ? null : new[] { additionalPath };
        var valid = LauncherSettings.TryValidatePaths(workspaceRoot, artifactsRoot, additionalPaths, out var error);
        Assert.True(valid);
        Assert.Null(error);
    }

    [Fact]
    public void TryValidatePaths_InvalidPathCharacters_ReturnsFalse()
    {
        var invalidPath = "C:\\work-agents\\bad" + Path.GetInvalidPathChars()[0] + "path";
        var valid = LauncherSettings.TryValidatePaths(invalidPath, null, null, out var error);
        Assert.False(valid);
        Assert.NotNull(error);
    }

    [Fact]
    public void TryValidateDatabasePath_InvalidPathCharacters_ReturnsFalse()
    {
        var invalidPath = "C:\\work-agents\\state" + Path.GetInvalidPathChars()[0] + ".db";

        var valid = LauncherSettings.TryValidateDatabasePath(invalidPath, out var error);

        Assert.False(valid);
        Assert.NotNull(error);
    }

    [Fact]
    public void Load_InvalidPathCharacters_FallsBackToDefaults()
    {
        Directory.CreateDirectory(_tempDir);
        var path = Path.Combine(_tempDir, "tray-settings.json");
        var invalidChar = Path.GetInvalidPathChars()[0];
        File.WriteAllText(path, $"{{\"workspaceRoot\": \"C:\\\\bad{invalidChar}path\"}}");

        var settings = LauncherSettings.Load(path);

        Assert.Null(settings.WorkspaceRoot);
    }

    [Fact]
    public void Load_LegacySingleDefinitionPath_MigratesToList()
    {
        Directory.CreateDirectory(_tempDir);
        var path = Path.Combine(_tempDir, "tray-settings.json");
        File.WriteAllText(path, "{\"additionalAgentDefinitionPath\": \"D:\\\\legacy-agents\"}");

        var settings = LauncherSettings.Load(path);

        Assert.Equal([@"D:\legacy-agents"], settings.AdditionalAgentDefinitionPaths);
        Assert.Equal(@"D:\legacy-agents", settings.AdditionalAgentDefinitionPath);
    }

    [Fact]
    public void TryParseAdditionalConfiguration_EmptyText_ReturnsEmptyDictionary()
    {
        var valid = LauncherSettings.TryParseAdditionalConfiguration("", out var parsed, out var error);
        Assert.True(valid);
        Assert.Empty(parsed);
        Assert.Null(error);
    }

    [Fact]
    public void TryParseAdditionalConfiguration_KeyValueLines_ParsesAll()
    {
        var text = "Runs:QueueCapacity=50\nGitAuth:AppId=12345\n";
        var valid = LauncherSettings.TryParseAdditionalConfiguration(text, out var parsed, out var error);

        Assert.True(valid);
        Assert.Null(error);
        Assert.Equal("50", parsed["Runs:QueueCapacity"]);
        Assert.Equal("12345", parsed["GitAuth:AppId"]);
    }

    [Fact]
    public void TryParseAdditionalConfiguration_IgnoresBlankLinesAndComments()
    {
        var text = "\n# comment\nRuns:QueueCapacity=50\n\n";
        var valid = LauncherSettings.TryParseAdditionalConfiguration(text, out var parsed, out var error);

        Assert.True(valid);
        Assert.Single(parsed);
        Assert.Equal("50", parsed["Runs:QueueCapacity"]);
    }

    [Theory]
    [InlineData("NoEqualsSignHere")]
    [InlineData("=ValueWithoutKey")]
    public void TryParseAdditionalConfiguration_MalformedLine_ReturnsFalse(string line)
    {
        var valid = LauncherSettings.TryParseAdditionalConfiguration(line, out _, out var error);
        Assert.False(valid);
        Assert.NotNull(error);
    }

    [Fact]
    public void FormatAdditionalConfiguration_RoundTripsWithParse()
    {
        var original = new Dictionary<string, string> { ["Runs:QueueCapacity"] = "50", ["GitAuth:AppId"] = "12345" };

        var formatted = LauncherSettings.FormatAdditionalConfiguration(original);
        var parsedOk = LauncherSettings.TryParseAdditionalConfiguration(formatted, out var parsed, out _);

        Assert.True(parsedOk);
        Assert.Equal(original, parsed);
    }

    [Fact]
    public void SaveThenLoad_RoundTripsAdditionalConfiguration()
    {
        var path = Path.Combine(_tempDir, "tray-settings.json");
        var settings = new LauncherSettings
        {
            AdditionalConfiguration = new Dictionary<string, string> { ["Runs:QueueCapacity"] = "50" },
        };

        settings.Save(path);
        var loaded = LauncherSettings.Load(path);

        Assert.Equal("50", loaded.AdditionalConfiguration?["Runs:QueueCapacity"]);
    }
}
