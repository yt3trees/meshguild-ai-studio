using Microsoft.Extensions.AI;

namespace WorkAgents.Agents.Tools;

/// <summary>Agentへ公開する関数ツールの登録情報。</summary>
public sealed record AgentToolRegistration(
    string Name,
    string Description,
    string Source,
    string Approval,
    AITool Tool);