using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using WorkAgents.Agents.Loading;
using WorkAgents.Agents.Tools;

namespace WorkAgents.UnitTests;

/// <summary>
/// <see cref="AgentToolCatalog.CreateWithPlugins"/> によるチーム固有ツールプラグイン(DLL)読み込みの検証
/// (specs/006-team-config-distribution、contracts/tool-plugin-contract.md)。
/// テスト用プラグインDLLは Roslyn でその場コンパイルし、実際のファイルベース読み込みを検証する。
/// </summary>
public sealed class AgentToolCatalogPluginTests
{
    [Fact]
    public void CreateWithPlugins_LoadsProviderFromPluginDirectory()
    {
        var pluginDir = CreateTempDir();
        try
        {
            CompilePluginAssembly(pluginDir, ValidPluginSource("plugin_tool", "meeting-agent"));

            var catalog = AgentToolCatalog.CreateWithPlugins(
                new NullServiceProvider(),
                [Definition("meeting-agent")],
                [pluginDir],
                NullLogger<AgentToolCatalog>.Instance);

            Assert.Contains(catalog.GetTools("meeting-agent"), tool => tool.Name == "plugin_tool");
            var result = Assert.Single(catalog.PluginLoadResults);
            Assert.Equal(ToolPluginLoadStatus.Loaded, result.LoadStatus);
            Assert.Contains("plugin_tool", result.ToolNames);
        }
        finally
        {
            Directory.Delete(pluginDir, recursive: true);
        }
    }

    [Fact]
    public void CreateWithPlugins_PluginOverridesStandardToolOfSameName()
    {
        var pluginDir = CreateTempDir();
        try
        {
            CompilePluginAssembly(pluginDir, ValidPluginSource("shared_tool", "meeting-agent"));

            var standardProvider = new StaticToolProvider("meeting-agent", Registration("shared_tool", "standard"));
            var catalog = AgentToolCatalog.CreateWithPlugins(
                new NullServiceProvider(),
                [Definition("meeting-agent")],
                [pluginDir],
                NullLogger<AgentToolCatalog>.Instance,
                extraProviders: [standardProvider]);

            var registration = Assert.Single(catalog.GetRegistrations("meeting-agent"), r => r.Name == "shared_tool");
            Assert.Equal("plugin", registration.Source);
        }
        finally
        {
            Directory.Delete(pluginDir, recursive: true);
        }
    }

    [Fact]
    public void CreateWithPlugins_RequiredApprovalToolFromPluginIsWrappedForApprovalFlow()
    {
        // 憲法II(Human-in-the-Loop): プラグイン由来の危険度の高いツールも既存の承認フローを必ず経由する
        // (contracts/tool-plugin-contract.md、specs/006-team-config-distribution T021)。
        var pluginDir = CreateTempDir();
        try
        {
            CompilePluginAssembly(pluginDir, RequiredApprovalPluginSource("dangerous_tool", "meeting-agent"));

            var catalog = AgentToolCatalog.CreateWithPlugins(
                new NullServiceProvider(),
                [Definition("meeting-agent")],
                [pluginDir],
                NullLogger<AgentToolCatalog>.Instance);

            var tool = Assert.Single(catalog.GetTools("meeting-agent"), t => t.Name == "dangerous_tool");
            Assert.IsType<ApprovalRequiredAIFunction>(tool);
        }
        finally
        {
            Directory.Delete(pluginDir, recursive: true);
        }
    }

    [Fact]
    public void CreateWithPlugins_DllAndScriptToolsCoexistInSameDirectory()
    {
        // User Story 4: DLLプラグインとスクリプトツールが同一の Agents:ToolPluginDirectories を共用し、
        // 両方とも DLLプラグインと同一の登録パイプラインに合流することを確認する(T035)。
        var pluginDir = CreateTempDir();
        try
        {
            CompilePluginAssembly(pluginDir, ValidPluginSource("dll_tool", "meeting-agent"));

            File.WriteAllText(Path.Combine(pluginDir, "script_tool.js"), "// no-op");
            File.WriteAllText(Path.Combine(pluginDir, "script_tool.tool.yaml"), """
                name: script_tool
                description: script tool description
                agentName: meeting-agent
                runtime: node
                entryPoint: script_tool.js
                approval: automatic
                """);

            var catalog = AgentToolCatalog.CreateWithPlugins(
                new NullServiceProvider(),
                [Definition("meeting-agent")],
                [pluginDir],
                NullLogger<AgentToolCatalog>.Instance);

            Assert.Contains(catalog.GetTools("meeting-agent"), tool => tool.Name == "dll_tool");
            Assert.Contains(catalog.GetTools("meeting-agent"), tool => tool.Name == "script_tool");
            Assert.Equal(2, catalog.PluginLoadResults.Count);
            Assert.All(catalog.PluginLoadResults, result => Assert.Equal(ToolPluginLoadStatus.Loaded, result.LoadStatus));
        }
        finally
        {
            Directory.Delete(pluginDir, recursive: true);
        }
    }

    [Fact]
    public void CreateWithPlugins_MissingDirectoryIsSkippedNotThrown()
    {
        var missingDir = Path.Combine(Path.GetTempPath(), "work-agents-tests", $"missing-{Guid.NewGuid():N}");

        var catalog = AgentToolCatalog.CreateWithPlugins(
            new NullServiceProvider(),
            [Definition("meeting-agent")],
            [missingDir],
            NullLogger<AgentToolCatalog>.Instance);

        Assert.DoesNotContain(catalog.GetTools("meeting-agent"), tool => tool.Name == "plugin_tool");
        Assert.Empty(catalog.PluginLoadResults);
    }

    [Fact]
    public void CreateWithPlugins_InvalidAssemblyIsRecordedAsFailedAndSkipped()
    {
        var pluginDir = CreateTempDir();
        try
        {
            File.WriteAllBytes(Path.Combine(pluginDir, "not-a-real-assembly.dll"), [0x00, 0x01, 0x02, 0x03]);

            var catalog = AgentToolCatalog.CreateWithPlugins(
                new NullServiceProvider(),
                [Definition("meeting-agent")],
                [pluginDir],
                NullLogger<AgentToolCatalog>.Instance);

            Assert.DoesNotContain(catalog.GetTools("meeting-agent"), tool => tool.Name == "plugin_tool");
            var result = Assert.Single(catalog.PluginLoadResults);
            Assert.Equal(ToolPluginLoadStatus.Failed, result.LoadStatus);
        }
        finally
        {
            Directory.Delete(pluginDir, recursive: true);
        }
    }

    private static string ValidPluginSource(string toolName, string agentName) => $$"""
        using System;
        using System.Collections.Generic;
        using System.Threading;
        using System.Threading.Tasks;
        using Microsoft.Extensions.AI;
        using WorkAgents.Agents.Tools;

        namespace TestToolPlugin;

        public sealed class SampleToolProvider : IAgentToolProvider
        {
            public string AgentName => "{{agentName}}";

            public IReadOnlyList<AgentToolRegistration> CreateTools(IServiceProvider services)
            {
                var tool = AIFunctionFactory.Create(
                    (Func<CancellationToken, Task<string>>)(_ => Task.FromResult("ok")),
                    "{{toolName}}",
                    "Plugin tool description.",
                    null);
                return new List<AgentToolRegistration>
                {
                    new AgentToolRegistration("{{toolName}}", "Plugin tool description.", "plugin", "automatic", tool),
                };
            }
        }
        """;

    private static string RequiredApprovalPluginSource(string toolName, string agentName) => $$"""
        using System;
        using System.Collections.Generic;
        using System.Threading;
        using System.Threading.Tasks;
        using Microsoft.Extensions.AI;
        using WorkAgents.Agents.Tools;

        namespace TestToolPlugin;

        public sealed class DangerousToolProvider : IAgentToolProvider
        {
            public string AgentName => "{{agentName}}";

            public IReadOnlyList<AgentToolRegistration> CreateTools(IServiceProvider services)
            {
                var tool = AIFunctionFactory.Create(
                    (Func<CancellationToken, Task<string>>)(_ => Task.FromResult("ok")),
                    "{{toolName}}",
                    "Dangerous plugin tool description.",
                    null);
                return new List<AgentToolRegistration>
                {
                    new AgentToolRegistration("{{toolName}}", "Dangerous plugin tool description.", "plugin", "required", tool),
                };
            }
        }
        """;

    private static void CompilePluginAssembly(string outputDir, string sourceCode)
    {
        var trustedAssemblies = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator);
        var references = trustedAssemblies
            .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path))
            .ToList();
        references.Add(MetadataReference.CreateFromFile(typeof(IAgentToolProvider).Assembly.Location));
        references.Add(MetadataReference.CreateFromFile(typeof(AIFunctionFactory).Assembly.Location));

        var syntaxTree = CSharpSyntaxTree.ParseText(sourceCode);
        var compilation = CSharpCompilation.Create(
            $"TestToolPlugin_{Guid.NewGuid():N}",
            [syntaxTree],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var dllPath = Path.Combine(outputDir, $"{compilation.AssemblyName}.dll");
        var emitResult = compilation.Emit(dllPath);
        if (!emitResult.Success)
        {
            var errors = string.Join("\n", emitResult.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
            throw new InvalidOperationException($"failed to compile test tool plugin: {errors}");
        }
    }

    private static string CreateTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "work-agents-tests", $"plugin-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static AgentDefinition Definition(string name) => new() { Name = name };

    private static AgentToolRegistration Registration(string name, string source)
    {
        var tool = AIFunctionFactory.Create(
            (Func<CancellationToken, Task<string>>)(_ => Task.FromResult("ok")),
            name,
            "Standard tool description.",
            null);
        return new AgentToolRegistration(name, "Standard tool description.", source, "automatic", tool);
    }

    private sealed class StaticToolProvider(string agentName, params AgentToolRegistration[] registrations) : IAgentToolProvider
    {
        public string AgentName { get; } = agentName;

        public IReadOnlyList<AgentToolRegistration> CreateTools(IServiceProvider services) => registrations;
    }

    private sealed class NullServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }
}
