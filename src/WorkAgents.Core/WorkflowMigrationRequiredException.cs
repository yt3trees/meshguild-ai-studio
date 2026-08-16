namespace WorkAgents.Core;

public sealed class WorkflowMigrationRequiredException : InvalidOperationException
{
    public WorkflowMigrationRequiredException(string workflowName)
        : base($"Workflow '{workflowName}' must be migrated to graph.yaml before execution.")
    {
        WorkflowName = workflowName;
    }

    public string WorkflowName { get; }
}
