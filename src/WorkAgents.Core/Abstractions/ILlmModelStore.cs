namespace WorkAgents.Core.Abstractions;

/// <summary>LLMモデル設定とエージェントごとのモデル割当を永続化する。</summary>
public interface ILlmModelStore
{
    Task<IReadOnlyList<LlmModelSettings>> ListAsync(CancellationToken ct = default);

    Task<LlmModelSettings?> GetAsync(string id, CancellationToken ct = default);

    Task<LlmModelSettings?> ResolveForAgentAsync(string agentName, CancellationToken ct = default);

    Task SaveAsync(LlmModelSettings settings, string? apiKey, string? clientSecret = null, CancellationToken ct = default);

    Task DeleteAsync(string id, CancellationToken ct = default);

    Task<string?> GetAgentModelIdAsync(string agentName, CancellationToken ct = default);

    Task AssignAgentAsync(string agentName, string? modelId, CancellationToken ct = default);
}