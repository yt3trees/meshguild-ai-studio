using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using WorkAgents.Core;
using WorkAgents.Harness.GitAuth;
using WorkAgents.Harness.Harness;

namespace WorkAgents.UnitTests;

public sealed class HarnessAgentFactoryTests
{
        [Fact]
        public void Workspace_yaml_maps_camel_case_shell_settings()
        {
                var workspace = WorkspaceYamlSerializer.Deserialize(
                        """
                        fileStore:
                            kind: workspace
                        shell:
                            confineWorkingDirectory: false
                            denyList:
                                - forbidden
                            timeoutSeconds: 42
                            maxOutputBytes: 8192
                        """);

                Assert.Equal("workspace", workspace.FileStore?.Kind);
                Assert.False(workspace.Shell?.ConfineWorkingDirectory);
                Assert.Equal(["forbidden"], workspace.Shell?.DenyList);
                Assert.Equal(42, workspace.Shell?.TimeoutSeconds);
                Assert.Equal(8192, workspace.Shell?.MaxOutputBytes);
        }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Create_exposes_cataloged_tools_for_shell_configuration(bool shellEnabled)
    {
        var chatClient = new CapturingChatClient();
        var factory = new HarnessAgentFactory(new ProfileOptions());
        var agent = factory.Create(
            chatClient,
            new HarnessAgentConfig
            {
                Name = "test-agent",
                Instructions = "Return a fixed response.",
                ShellEnabled = shellEnabled,
                WorkingDirectory = Path.GetTempPath(),
            });

        await agent.RunAsync("test request");

        Assert.NotNull(chatClient.LastOptions);
        Assert.Equal(
            HarnessToolCatalog.List(shellEnabled, skillsEnabled: false).Select(tool => tool.Name).Order(),
            chatClient.LastOptions.Tools!.Select(tool => tool.Name).Order());
    }

    [Fact]
    public async Task Create_keeps_custom_tools_alongside_shell_tool()
    {
        var customTool = AIFunctionFactory.Create(
            (Func<CancellationToken, Task<string>>)(_ => Task.FromResult("ok")),
            "get_sss",
            "Return the SSS sample result.",
            null);
        var chatClient = new CapturingChatClient();
        var factory = new HarnessAgentFactory(new ProfileOptions());
        var agent = factory.Create(
            chatClient,
            new HarnessAgentConfig
            {
                Name = "test-agent",
                Instructions = "Return a fixed response.",
                ShellEnabled = true,
                CustomTools = [customTool],
                WorkingDirectory = Path.GetTempPath(),
            });

        await agent.RunAsync("test request");

        Assert.Contains(chatClient.LastOptions!.Tools!, tool => tool.Name == "get_sss");
        Assert.Contains(chatClient.LastOptions.Tools!, tool => tool.Name == "run_shell");
    }

    [Fact]
    public async Task Create_usesWorkspaceYamlFileStoreRoot_whenWorkingDirectoryNotSet()
    {
        var agentFolder = Path.Combine(Path.GetTempPath(), $"work-agents-agent-{Guid.NewGuid():N}");
        var configuredRoot = Path.Combine(Path.GetTempPath(), $"work-agents-fsroot-{Guid.NewGuid():N}");
        Directory.CreateDirectory(agentFolder);
        File.WriteAllText(
            Path.Combine(agentFolder, "workspace.yaml"),
            $"""
            fileStore:
                kind: workspace
                root: {configuredRoot}
            """);

        try
        {
            var chatClient = new CapturingChatClient();
            var factory = new HarnessAgentFactory(new ProfileOptions
            {
                WorkspaceRoot = Path.Combine(Path.GetTempPath(), $"work-agents-unused-{Guid.NewGuid():N}"),
            });
            var agent = factory.Create(
                chatClient,
                new HarnessAgentConfig
                {
                    Name = "test-agent",
                    Instructions = "Return a fixed response.",
                    AgentFolderPath = agentFolder,
                    FileStoreKind = "workspace",
                    ShellEnabled = false,
                });

            await agent.RunAsync("test request");

            var agentDir = Path.Combine(configuredRoot, "test-agent");
            Assert.True(Directory.Exists(agentDir), $"expected working directory under configured root: {agentDir}");
            Assert.NotEmpty(Directory.GetDirectories(agentDir));
        }
        finally
        {
            Directory.Delete(agentFolder, recursive: true);
            if (Directory.Exists(configuredRoot))
            {
                Directory.Delete(configuredRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Create_usesExplicitWorkingDirectory_withoutCreatingAgentFallbackDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), $"work-agents-explicit-{Guid.NewGuid():N}");
        var explicitDirectory = Path.Combine(root, "missions", "mission", "work");
        var fallbackRoot = Path.Combine(root, "fallback");
        Directory.CreateDirectory(explicitDirectory);

        try
        {
            var chatClient = new CapturingChatClient();
            var factory = new HarnessAgentFactory(new ProfileOptions { WorkspaceRoot = fallbackRoot });
            var agent = factory.Create(
                chatClient,
                new HarnessAgentConfig
                {
                    Name = "test-agent",
                    Instructions = "Return a fixed response.",
                    FileStoreKind = "workspace",
                    WorkingDirectory = explicitDirectory,
                });

            await agent.RunAsync("test request");

            Assert.True(Directory.Exists(explicitDirectory));
            Assert.False(Directory.Exists(Path.Combine(fallbackRoot, "test-agent")));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Create_exposes_load_skill_only_when_skill_paths_are_configured()
    {
        var root = Path.Combine(Path.GetTempPath(), $"work-agents-skills-{Guid.NewGuid():N}");
        var skillDirectory = Path.Combine(root, "meeting-minutes");
        Directory.CreateDirectory(skillDirectory);
        File.WriteAllText(
            Path.Combine(skillDirectory, "SKILL.md"),
            """
            ---
            name: meeting-minutes
            description: Format meeting minutes.
            ---
            # Meeting minutes
            """);

        try
        {
            var chatClient = new CapturingChatClient();
            var factory = new HarnessAgentFactory(new ProfileOptions());
            var agent = factory.Create(
                chatClient,
                new HarnessAgentConfig
                {
                    Name = "skill-agent",
                    Instructions = "Use the configured skill when needed.",
                    SkillPaths = [skillDirectory],
                    WorkingDirectory = root,
                });

            await agent.RunAsync("test request");

            Assert.Equal(
                HarnessToolCatalog.List(shellEnabled: false, skillsEnabled: true).Select(tool => tool.Name).Order(),
                chatClient.LastOptions!.Tools!.Select(tool => tool.Name).Order());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Create_initializesGitAuth_beforeAttachingShell_whenShellEnabled()
    {
        var chatClient = new CapturingChatClient();
        var gitAuth = new FakeGitAuth();
        var factory = new HarnessAgentFactory(new ProfileOptions(), logger: null, gitAuth: gitAuth);
        var agent = factory.Create(
            chatClient,
            new HarnessAgentConfig
            {
                Name = "test-agent",
                Instructions = "Return a fixed response.",
                ShellEnabled = true,
                WorkingDirectory = Path.GetTempPath(),
            });

        await agent.RunAsync("test request");

        Assert.Equal(1, gitAuth.InitializeCallCount);
    }

    [Fact]
    public async Task Create_doesNotInitializeGitAuth_whenShellDisabled()
    {
        var chatClient = new CapturingChatClient();
        var gitAuth = new FakeGitAuth();
        var factory = new HarnessAgentFactory(new ProfileOptions(), logger: null, gitAuth: gitAuth);
        var agent = factory.Create(
            chatClient,
            new HarnessAgentConfig
            {
                Name = "test-agent",
                Instructions = "Return a fixed response.",
                ShellEnabled = false,
                WorkingDirectory = Path.GetTempPath(),
            });

        await agent.RunAsync("test request");

        Assert.Equal(0, gitAuth.InitializeCallCount);
    }

    [Fact]
    public async Task Create_stillAttachesShell_whenGitAuthInitializationFails()
    {
        var chatClient = new CapturingChatClient();
        var gitAuth = new FakeGitAuth { ThrowOnInitialize = true };
        var factory = new HarnessAgentFactory(new ProfileOptions(), logger: null, gitAuth: gitAuth);
        var agent = factory.Create(
            chatClient,
            new HarnessAgentConfig
            {
                Name = "test-agent",
                Instructions = "Return a fixed response.",
                ShellEnabled = true,
                WorkingDirectory = Path.GetTempPath(),
            });

        await agent.RunAsync("test request");

        Assert.Equal(1, gitAuth.InitializeCallCount);
        Assert.Contains(chatClient.LastOptions!.Tools!, tool => tool.Name == "run_shell");
    }

    private sealed class FakeGitAuth : IGitAuth
    {
        public int InitializeCallCount { get; private set; }

        public bool ThrowOnInitialize { get; init; }

        public DateTimeOffset? TokenExpiresAt { get; private set; }

        public Task InitializeAsync(CancellationToken cancellationToken = default)
        {
            InitializeCallCount++;
            if (ThrowOnInitialize)
            {
                // GitHubAppTokenMinter が実際に投げる例外を模す。秘密鍵・トークン文字列は含めない。
                throw new InvalidOperationException("GitHub App private key not found in secret store (name='github-app-private-key').");
            }

            TokenExpiresAt = DateTimeOffset.UtcNow.AddMinutes(9);
            return Task.CompletedTask;
        }

        public Task RefreshAsync(CancellationToken cancellationToken = default) => InitializeAsync(cancellationToken);
    }

    private sealed class CapturingChatClient : IChatClient
    {
        public ChatOptions? LastOptions { get; private set; }

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            LastOptions = options;
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "done")));
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            LastOptions = options;
            yield return new ChatResponseUpdate(ChatRole.Assistant, "done");
            await Task.CompletedTask;
        }

        public object? GetService(Type serviceType, object? serviceKey = null)
            => serviceType.IsInstanceOfType(this) ? this : null;

        public void Dispose()
        {
        }
    }
}
