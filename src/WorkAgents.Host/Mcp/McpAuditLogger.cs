using Microsoft.Extensions.Logging;

namespace WorkAgents.Host.Mcp;

public sealed record McpAuditEvent(
    string EventName,
    string ProtocolVersion,
    string Operation,
    string Outcome,
    string? RequestId = null,
    string? ClientName = null,
    string? TargetKind = null,
    string? TargetName = null,
    string? MissionId = null,
    string? ErrorCode = null,
    long DurationMs = 0,
    long ResponseBytes = 0);

public sealed class McpAuditLogger(ILogger<McpAuditLogger> logger)
{
    public void Record(McpAuditEvent audit)
    {
        logger.LogInformation(
            "mcp event={EventName} protocol={ProtocolVersion} operation={Operation} outcome={Outcome} request={RequestId} client={ClientName} targetKind={TargetKind} target={TargetName} mission={MissionId} error={ErrorCode} durationMs={DurationMs} responseBytes={ResponseBytes}",
            audit.EventName,
            McpRedaction.SafeName(audit.ProtocolVersion),
            McpRedaction.SafeName(audit.Operation),
            McpRedaction.SafeName(audit.Outcome),
            McpRedaction.SafeName(audit.RequestId),
            McpRedaction.SafeName(audit.ClientName),
            McpRedaction.SafeName(audit.TargetKind),
            McpRedaction.SafeName(audit.TargetName),
            McpRedaction.SafeName(audit.MissionId),
            McpRedaction.SafeErrorCode(audit.ErrorCode),
            audit.DurationMs,
            audit.ResponseBytes);
    }
}
