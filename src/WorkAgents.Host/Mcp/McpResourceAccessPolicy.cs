using System.Text;
using Microsoft.Extensions.Options;
using ModelContextProtocol;

namespace WorkAgents.Host.Mcp;

public sealed class McpResourceAccessPolicy(IOptions<McpOptions> options)
{
    private readonly McpOptions _options = options.Value;

    public static bool IsSafeIdentifier(string? value)
        => !string.IsNullOrWhiteSpace(value)
            && value.Length <= 128
            && value.All(character => char.IsLetterOrDigit(character) || character is '-' or '_' or '.');

    public static bool IsTextContentType(string? contentType)
        => contentType is "text/plain" or "text/markdown" or "application/json" or "text/csv" or "text/html";

    public bool IsResponseSizeAllowed(long bytes) => bytes >= 0 && bytes <= 8_388_608 && bytes <= _options.MaxResponseBytes;

    public bool IsArtifactSizeAllowed(long bytes) => bytes >= 0 && bytes <= _options.MaxArtifactBytes;

    public async Task<string> ReadTextAsync(Stream stream, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (stream.CanSeek && !IsArtifactSizeAllowed(stream.Length))
        {
            throw new McpException("[artifact_unavailable] Artifact exceeds the configured size limit.");
        }

        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: false);
        var text = await reader.ReadToEndAsync(ct);
        if (!IsResponseSizeAllowed(Encoding.UTF8.GetByteCount(text)))
        {
            throw new McpException("[artifact_unavailable] Resource exceeds the configured response limit.");
        }

        return text;
    }
}
