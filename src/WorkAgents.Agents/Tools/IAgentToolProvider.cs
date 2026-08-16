namespace WorkAgents.Agents.Tools;

/// <summary>特定Agentへ公開する関数ツールを起動時に生成するProvider。</summary>
public interface IAgentToolProvider
{
    string AgentName { get; }

    IReadOnlyList<AgentToolRegistration> CreateTools(IServiceProvider services);
}