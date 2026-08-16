namespace WorkAgents.Host.Mcp;

/// <summary>Configuration for the optional local MCP endpoint.</summary>
public sealed class McpOptions
{
    public const string SectionName = "Mcp";

    public bool Enabled { get; set; }

    public string EndpointPath { get; set; } = "/mcp";

    /// <summary>
    /// Modern requests are stateless. This value enables the SDK compatibility mode for
    /// legacy initialize clients when the protocol spike confirms it is safe.
    /// </summary>
    public string SessionMode { get; set; } = "StatefulForInitializeClients";

    public List<string> AllowedOrigins { get; set; } = [];

    public int MaxPageSize { get; set; } = 100;

    public long MaxResponseBytes { get; set; } = 8_388_608;

    public long MaxArtifactBytes { get; set; } = 5_242_880;

    public int RequestTimeoutSeconds { get; set; } = 30;

    public int IdempotencyRetentionSeconds { get; set; } = 86_400;
}
