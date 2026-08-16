namespace WorkAgents.Agents;

using WorkAgents.Core;

/// <summary>ワークフロー一覧(選択肢)の軽量ビュー(5.13.1)。</summary>
public sealed record WorkflowView(
    string Name,
    string DisplayName,
    string Description,
    int StepCount,
    string? ScheduleCron,
    bool HasSchedule,
    string SourceLabel = "standard");

/// <summary>
/// ワークフロー定義の読み取り専用参照(5.13.1)。<see cref="IAgentRegistry"/> 実装が兼務することを想定。
/// </summary>
public interface IWorkflowRegistry
{
    IReadOnlyList<WorkflowView> ListWorkflows();

    WorkflowDefinition? GetWorkflow(string name);
}