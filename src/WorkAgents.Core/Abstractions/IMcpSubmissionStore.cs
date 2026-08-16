using WorkAgents.Core.Missions;

namespace WorkAgents.Core.Abstractions;

public interface IMcpSubmissionStore
{
    Task<McpSubmission?> GetAsync(string requestKey, CancellationToken ct = default);

    Task<bool> TryCreateAsync(McpSubmission submission, CancellationToken ct = default);

    Task DeleteExpiredAsync(DateTimeOffset before, CancellationToken ct = default);
}
