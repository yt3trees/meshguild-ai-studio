using Microsoft.Extensions.AI;
using WorkAgents.Agents.Tools;

namespace WorkAgents.Agents.MeetingAgent.Tools;

/// <summary>meeting-agent専用の関数ツールProvider。</summary>
public sealed class MeetingAgentToolProvider : IAgentToolProvider
{
    public string AgentName => "meeting-agent";

    public IReadOnlyList<AgentToolRegistration> CreateTools(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);

        var function = AIFunctionFactory.Create(
            (Func<string, CancellationToken, Task<GetSssResult>>)GetSssTool.ExecuteAsync,
            "get_sss",
            "subjectを受け取ってSSSの結果を返すサンプル。SSS連携が設定済みの場合だけ実行し、subjectとresultをJSONで返す。",
            null);

        return
        [
            new AgentToolRegistration(
                "get_sss",
                "subjectを受け取ってSSSの結果を返すサンプル。SSS連携が設定済みの場合だけ実行し、subjectとresultをJSONで返す。",
                "custom",
                "automatic",
                function),
        ];
    }
}