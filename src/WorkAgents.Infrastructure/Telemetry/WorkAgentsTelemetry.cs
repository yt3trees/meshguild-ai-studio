using System.Diagnostics;

namespace WorkAgents.Infrastructure.Telemetry;

public static class WorkAgentsTelemetry
{
    public const string ActivitySourceName = "WorkAgents";

    public static readonly ActivitySource ActivitySource = new(ActivitySourceName);
}