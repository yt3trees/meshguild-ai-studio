using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace WorkAgents.Host.Mcp;

public sealed record McpValidationIssue(string Code, string Message, int StatusCode = StatusCodes.Status400BadRequest);

public sealed class McpOptionsValidator : IValidateOptions<McpOptions>
{
    public ValidateOptionsResult Validate(string? name, McpOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(options.EndpointPath)
            || !options.EndpointPath.StartsWith("/", StringComparison.Ordinal)
            || options.EndpointPath.StartsWith("//", StringComparison.Ordinal)
            || options.EndpointPath.Contains('?', StringComparison.Ordinal)
            || options.EndpointPath.Contains('#', StringComparison.Ordinal))
        {
            errors.Add("Mcp:EndpointPath must be a relative path beginning with '/'.");
        }

        if (!string.Equals(options.SessionMode, "Stateless", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(options.SessionMode, "Stateful", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(options.SessionMode, "StatefulForInitializeClients", StringComparison.OrdinalIgnoreCase))
        {
            errors.Add("Mcp:SessionMode must be Stateless, Stateful, or StatefulForInitializeClients.");
        }

        if (options.MaxPageSize < 1 || options.MaxPageSize > 10_000)
        {
            errors.Add("Mcp:MaxPageSize must be between 1 and 10000.");
        }

        if (options.MaxResponseBytes < 1 || options.MaxResponseBytes > 100L * 1024 * 1024)
        {
            errors.Add("Mcp:MaxResponseBytes must be between 1 and 104857600.");
        }

        if (options.MaxArtifactBytes < 1 || options.MaxArtifactBytes > options.MaxResponseBytes)
        {
            errors.Add("Mcp:MaxArtifactBytes must be positive and no greater than MaxResponseBytes.");
        }

        if (options.RequestTimeoutSeconds < 1 || options.RequestTimeoutSeconds > 3600)
        {
            errors.Add("Mcp:RequestTimeoutSeconds must be between 1 and 3600.");
        }

        if (options.IdempotencyRetentionSeconds < 1 || options.IdempotencyRetentionSeconds > 31_536_000)
        {
            errors.Add("Mcp:IdempotencyRetentionSeconds must be between 1 and 31536000.");
        }

        foreach (var origin in options.AllowedOrigins)
        {
            if (string.IsNullOrWhiteSpace(origin)
                || origin.Contains('*', StringComparison.Ordinal)
                || !Uri.TryCreate(origin, UriKind.Absolute, out var uri)
                || uri.Scheme is not ("http" or "https")
                || !string.IsNullOrEmpty(uri.AbsolutePath.Trim('/'))
                || !string.IsNullOrEmpty(uri.Query)
                || !string.IsNullOrEmpty(uri.Fragment))
            {
                errors.Add("Mcp:AllowedOrigins must contain exact http/https origins without wildcards, paths, queries, or fragments.");
                break;
            }
        }

        return errors.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(errors);
    }
}

public sealed class McpRequestValidator(IOptions<McpOptions> options)
{
    private readonly McpOptions _options = options.Value;

    public McpValidationIssue? ValidateHttpRequest(HttpContext context)
    {
        var origin = context.Request.Headers.Origin.ToString();
        if (!string.IsNullOrWhiteSpace(origin) && !IsOriginAllowed(origin))
        {
            return new McpValidationIssue(
                "origin_not_allowed",
                "The request origin is not allowed.",
                StatusCodes.Status403Forbidden);
        }

        if (context.Request.ContentLength > _options.MaxResponseBytes)
        {
            return new McpValidationIssue("request_too_large", "The request exceeds the configured size limit.");
        }

        return null;
    }

    public bool IsOriginAllowed(string origin)
    {
        if (!Uri.TryCreate(origin, UriKind.Absolute, out var requested)
            || requested.Scheme is not ("http" or "https"))
        {
            return false;
        }

        if (_options.AllowedOrigins.Count > 0)
        {
            return _options.AllowedOrigins.Any(configured =>
                Uri.TryCreate(configured, UriKind.Absolute, out var allowed)
                && Uri.Compare(requested, allowed, UriComponents.SchemeAndServer, UriFormat.Unescaped, StringComparison.OrdinalIgnoreCase) == 0);
        }

        return requested.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            || requested.Host.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase)
            || requested.Host.Equals("::1", StringComparison.OrdinalIgnoreCase);
    }

    public int ClampPageSize(int? requested)
        => Math.Clamp(requested ?? _options.MaxPageSize, 1, _options.MaxPageSize);

    public bool IsResponseSizeAllowed(long byteCount) => byteCount is >= 0 and <= long.MaxValue && byteCount <= _options.MaxResponseBytes;

    public bool IsArtifactSizeAllowed(long byteCount) => byteCount is >= 0 and <= long.MaxValue && byteCount <= _options.MaxArtifactBytes;
}
