using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using WorkAgents.Agents;
using WorkAgents.Agents.Loading;
using WorkAgents.Core;

namespace WorkAgents.UnitTests;

public sealed class LlmAgentFactoryTests
{
    [Theory]
    [InlineData("ChatCompletion")]
    [InlineData("Responses")]
    public void CreateOpenAIWithApiKey_BuildsAnAgentWithoutNetworkAccess(string api)
    {
        var factory = new LlmAgentFactory(NullLogger<LlmAgentFactory>.Instance);
        var agent = factory.Create(
            new AgentDefinition
            {
                Name = "test-agent",
                Instructions = "Answer test prompts.",
            },
            new LlmModelSettings
            {
                Id = "openai-api-key",
                Name = "OpenAI API key",
                Provider = LlmProvider.OpenAI,
                DeploymentName = "gpt-4.1",
                Api = api,
                ApiKey = "test-api-key",
            });

        Assert.IsType<ChatClientAgent>(agent);
    }

    [Fact]
    public void CreateOpenRouterWithApiKey_BuildsAnAgentWithoutNetworkAccess()
    {
        var factory = new LlmAgentFactory(NullLogger<LlmAgentFactory>.Instance);
        var agent = factory.Create(
            new AgentDefinition
            {
                Name = "test-agent",
                Instructions = "Answer test prompts.",
            },
            new LlmModelSettings
            {
                Id = "openrouter-api-key",
                Name = "OpenRouter API key",
                Provider = LlmProvider.OpenRouter,
                DeploymentName = "openai/gpt-4.1",
                ApiKey = "test-api-key",
            });

        Assert.IsType<ChatClientAgent>(agent);
    }

    [Fact]
    public void CreateAmazonBedrockWithRegion_BuildsAnAgentWithoutNetworkAccess()
    {
        var factory = new LlmAgentFactory(NullLogger<LlmAgentFactory>.Instance);
        var agent = factory.Create(
            new AgentDefinition
            {
                Name = "test-agent",
                Instructions = "Answer test prompts.",
            },
            new LlmModelSettings
            {
                Id = "bedrock-model",
                Name = "Amazon Bedrock model",
                Provider = LlmProvider.AmazonBedrock,
                Endpoint = "us-east-1",
                DeploymentName = "amazon.nova-lite-v1:0",
            });

        Assert.IsType<ChatClientAgent>(agent);
    }

    [Fact]
    public void CreateFoundryWithApiKey_BuildsAnAgentWithoutNetworkAccess()
    {
        var factory = new LlmAgentFactory(NullLogger<LlmAgentFactory>.Instance);
        var agent = factory.Create(
            new AgentDefinition
            {
                Name = "test-agent",
                Instructions = "Answer test prompts.",
            },
            new LlmModelSettings
            {
                Id = "foundry-api-key",
                Name = "Foundry API key",
                ProjectEndpoint = "https://example.test/api/projects/test",
                DeploymentName = "test-model",
                ApiKey = "test-api-key",
            });

        Assert.IsType<ChatClientAgent>(agent);
    }
}
