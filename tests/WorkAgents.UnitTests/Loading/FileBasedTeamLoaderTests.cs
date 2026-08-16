using WorkAgents.Agents.Loading;

namespace WorkAgents.UnitTests.Loading;

public sealed class FileBasedTeamLoaderTests
{
    private static readonly string[] KnownAgents = { "orchestrator-agent", "dev-agent", "test-agent", "spec-research-agent" };

    [Fact]
    public void Load_ValidTeam_Succeeds()
    {
        var dir = CreateTeamDir("demo-team", """
            version: 1
            name: demo-team
            orchestrator:
              agent: orchestrator-agent
            members:
              - agent: dev-agent
              - agent: test-agent
            """);
        try
        {
            var loader = new FileBasedTeamLoader();
            var team = loader.Load(dir, KnownAgents);

            Assert.Equal("demo-team", team.Name);
            Assert.Equal(2, team.Members.Count);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(dir)!, recursive: true);
        }
    }

    [Fact]
    public void Load_ChannelsDefaultDirect_ParsesToDirect()
    {
        var dir = CreateTeamDir("demo-team", """
            version: 1
            name: demo-team
            orchestrator:
              agent: orchestrator-agent
            members:
              - agent: dev-agent
              - agent: test-agent
            channels:
              default: direct
            """);
        try
        {
            var loader = new FileBasedTeamLoader();
            var team = loader.Load(dir, KnownAgents);

            Assert.Equal(WorkAgents.Core.Teams.ChannelDefault.Direct, team.ChannelsDefault);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(dir)!, recursive: true);
        }
    }

    [Fact]
    public void Load_ChannelsDefaultUnknownValue_Throws()
    {
        var dir = CreateTeamDir("demo-team", """
            version: 1
            name: demo-team
            orchestrator:
              agent: orchestrator-agent
            members:
              - agent: dev-agent
            channels:
              default: sometimes
            """);
        try
        {
            var loader = new FileBasedTeamLoader();
            var ex = Assert.Throws<TeamValidationException>(() => loader.Load(dir, KnownAgents));
            Assert.Contains("unknown channels.default", ex.Message);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(dir)!, recursive: true);
        }
    }

    [Fact]
    public void Load_UnknownAgentReference_Throws()
    {
        var dir = CreateTeamDir("demo-team", """
            version: 1
            name: demo-team
            orchestrator:
              agent: orchestrator-agent
            members:
              - agent: unknown-agent
            """);
        try
        {
            var loader = new FileBasedTeamLoader();
            var ex = Assert.Throws<TeamValidationException>(() => loader.Load(dir, KnownAgents));
            Assert.Contains("unknown agent", ex.Message);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(dir)!, recursive: true);
        }
    }

    [Fact]
    public void Load_DuplicateMember_Throws()
    {
        var dir = CreateTeamDir("demo-team", """
            version: 1
            name: demo-team
            orchestrator:
              agent: orchestrator-agent
            members:
              - agent: dev-agent
              - agent: dev-agent
            """);
        try
        {
            var loader = new FileBasedTeamLoader();
            var ex = Assert.Throws<TeamValidationException>(() => loader.Load(dir, KnownAgents));
            Assert.Contains("duplicate member", ex.Message);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(dir)!, recursive: true);
        }
    }

    [Fact]
    public void Load_LimitExceeded_Throws()
    {
        var dir = CreateTeamDir("demo-team", """
            version: 1
            name: demo-team
            orchestrator:
              agent: orchestrator-agent
              maxInstances: 1
            members:
              - agent: dev-agent
                maxInstances: 3
              - agent: test-agent
                maxInstances: 3
            limits:
              maxParallelInstances: 4
            """);
        try
        {
            var loader = new FileBasedTeamLoader();
            var ex = Assert.Throws<TeamValidationException>(() => loader.Load(dir, KnownAgents));
            Assert.Contains("exceed team parallel limit", ex.Message);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(dir)!, recursive: true);
        }
    }

    [Fact]
    public void Load_UnknownKey_Throws()
    {
        var dir = CreateTeamDir("demo-team", """
            version: 1
            name: demo-team
            orchestrator:
              agent: orchestrator-agent
            members:
              - agent: dev-agent
            notARealKey: true
            """);
        try
        {
            var loader = new FileBasedTeamLoader();
            var ex = Assert.Throws<TeamValidationException>(() => loader.Load(dir, KnownAgents));
            Assert.Contains("unknown key", ex.Message);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(dir)!, recursive: true);
        }
    }

    [Fact]
    public void Load_NameMismatchFolder_Throws()
    {
        var dir = CreateTeamDir("demo-team", """
            version: 1
            name: some-other-name
            orchestrator:
              agent: orchestrator-agent
            members:
              - agent: dev-agent
            """);
        try
        {
            var loader = new FileBasedTeamLoader();
            var ex = Assert.Throws<TeamValidationException>(() => loader.Load(dir, KnownAgents));
            Assert.Contains("team name must match folder name", ex.Message);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(dir)!, recursive: true);
        }
    }

    private static string CreateTeamDir(string teamName, string yaml)
    {
        var root = Path.Combine(Path.GetTempPath(), "work-agents-tests", $"{Guid.NewGuid():N}");
        var teamDir = Path.Combine(root, teamName);
        Directory.CreateDirectory(teamDir);
        File.WriteAllText(Path.Combine(teamDir, "team.yaml"), yaml);
        return teamDir;
    }
}
