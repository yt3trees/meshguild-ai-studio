using WorkAgents.Core.Graphs;

namespace WorkAgents.Orchestration.Graph.NodeHandlers;

/// <summary>Extension point for graph node semantics. GraphExecutor supplies the deterministic dispatch loop.</summary>
public interface IGraphNodeHandler
{
    NodeKind Kind { get; }

    Task<string> ExecuteAsync(GraphNode node, string input, CancellationToken ct = default);
}

public abstract class GraphNodeHandlerBase(NodeKind kind) : IGraphNodeHandler
{
    public NodeKind Kind { get; } = kind;

    public abstract Task<string> ExecuteAsync(GraphNode node, string input, CancellationToken ct = default);
}

public sealed class AgentNodeHandler : GraphNodeHandlerBase
{
    private readonly Func<GraphNode, string, CancellationToken, Task<string>> _handler;
    public AgentNodeHandler(Func<GraphNode, string, CancellationToken, Task<string>> handler) : base(NodeKind.Agent) => _handler = handler;
    public override Task<string> ExecuteAsync(GraphNode node, string input, CancellationToken ct = default) => _handler(node, input, ct);
}

public sealed class TeamNodeHandler : GraphNodeHandlerBase
{
    private readonly Func<GraphNode, string, CancellationToken, Task<string>> _handler;
    public TeamNodeHandler(Func<GraphNode, string, CancellationToken, Task<string>> handler) : base(NodeKind.Team) => _handler = handler;
    public override Task<string> ExecuteAsync(GraphNode node, string input, CancellationToken ct = default) => _handler(node, input, ct);
}

public sealed class CodeNodeHandler : GraphNodeHandlerBase
{
    private readonly Func<GraphNode, string, CancellationToken, Task<string>> _handler;
    public CodeNodeHandler(Func<GraphNode, string, CancellationToken, Task<string>> handler) : base(NodeKind.Code) => _handler = handler;
    public override Task<string> ExecuteAsync(GraphNode node, string input, CancellationToken ct = default) => _handler(node, input, ct);
}

public sealed class ApprovalNodeHandler : GraphNodeHandlerBase
{
    private readonly Func<GraphNode, string, CancellationToken, Task<string>> _handler;
    public ApprovalNodeHandler(Func<GraphNode, string, CancellationToken, Task<string>> handler) : base(NodeKind.Approval) => _handler = handler;
    public override Task<string> ExecuteAsync(GraphNode node, string input, CancellationToken ct = default) => _handler(node, input, ct);
}

public sealed class BranchNodeHandler : GraphNodeHandlerBase
{
    public BranchNodeHandler() : base(NodeKind.Branch) { }
    public override Task<string> ExecuteAsync(GraphNode node, string input, CancellationToken ct = default) => Task.FromResult(input);
}

public sealed class ParallelNodeHandler : GraphNodeHandlerBase
{
    public ParallelNodeHandler() : base(NodeKind.Parallel) { }
    public override Task<string> ExecuteAsync(GraphNode node, string input, CancellationToken ct = default) => Task.FromResult(input);
}

public sealed class JoinNodeHandler : GraphNodeHandlerBase
{
    public JoinNodeHandler() : base(NodeKind.Join) { }
    public override Task<string> ExecuteAsync(GraphNode node, string input, CancellationToken ct = default) => Task.FromResult(input);
}

public sealed class LoopNodeHandler : GraphNodeHandlerBase
{
    private readonly Func<GraphNode, string, CancellationToken, Task<string>> _handler;
    public LoopNodeHandler(Func<GraphNode, string, CancellationToken, Task<string>> handler) : base(NodeKind.Loop) => _handler = handler;
    public override Task<string> ExecuteAsync(GraphNode node, string input, CancellationToken ct = default) => _handler(node, input, ct);
}

public sealed class SubgraphNodeHandler : GraphNodeHandlerBase
{
    private readonly Func<GraphNode, string, CancellationToken, Task<string>> _handler;
    public SubgraphNodeHandler(Func<GraphNode, string, CancellationToken, Task<string>> handler) : base(NodeKind.Subgraph) => _handler = handler;
    public override Task<string> ExecuteAsync(GraphNode node, string input, CancellationToken ct = default) => _handler(node, input, ct);
}
