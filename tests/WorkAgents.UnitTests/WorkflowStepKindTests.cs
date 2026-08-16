using Microsoft.Extensions.Logging.Abstractions;
using WorkAgents.Agents.Loading;
using WorkAgents.Core;
using WorkAgents.Core.Abstractions;
using WorkAgents.Infrastructure.Workflows;

namespace WorkAgents.UnitTests;

public sealed class WorkflowStepKindTests
{
    [Theory]
    [InlineData(null, WorkflowStepKind.Agent)]
    [InlineData("", WorkflowStepKind.Agent)]
    [InlineData("agent", WorkflowStepKind.Agent)]
    [InlineData("code", WorkflowStepKind.Code)]
    [InlineData("APPROVE", WorkflowStepKind.Approve)]
    public void ParseKindNormalizes(string? input, WorkflowStepKind expected)
    {
        Assert.Equal(expected, WorkflowYamlSerializer.ParseKind(input));
    }

    [Fact]
    public void ParseKindRejectsUnknown()
    {
        Assert.Throws<InvalidOperationException>(() => WorkflowYamlSerializer.ParseKind("weather"));
    }

    [Fact]
    public async Task RoslynRunnerReturnsAnonymousTypeAsync()
    {
        var runner = new RoslynWorkflowScriptRunner(NullLogger<RoslynWorkflowScriptRunner>.Instance);
        var inputs = new Dictionary<string, object?>
        {
            ["minutes"] = "hello",
            ["workflow.input"] = "seed text",
        };

        var result = await runner.RunAsync("return new { title = \"demo.md\", body = (string)Inputs[\"minutes\"] };", inputs);

        Assert.NotNull(result);
        var dict = ToDictionary(result!);
        Assert.Equal("demo.md", dict["title"]);
        Assert.Equal("hello", dict["body"]);
        Assert.Equal("seed text", inputs["workflow.input"]);
    }

    [Fact]
    public async Task RoslynRunnerCanWriteFileViaSystemIOAsync()
    {
        var runner = new RoslynWorkflowScriptRunner(NullLogger<RoslynWorkflowScriptRunner>.Instance);
        var file = Path.Combine(Path.GetTempPath(), "work-agents-tests", "code-step", $"{Guid.NewGuid():N}.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        try
        {
            await runner.RunAsync($"System.IO.File.WriteAllText(@\"{file}\", \"hello\"); return new {{ saved = true }};",
                new Dictionary<string, object?>());
            Assert.Equal("hello", File.ReadAllText(file));
        }
        finally
        {
            if (Directory.Exists(Path.GetDirectoryName(file)))
            {
                Directory.Delete(Path.GetDirectoryName(file)!, recursive: true);
            }
        }
    }

    private static IReadOnlyDictionary<string, object?> ToDictionary(object raw)
    {
        var dict = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var prop in raw.GetType().GetProperties())
        {
            dict[prop.Name] = prop.GetValue(raw);
        }
        return dict;
    }
}