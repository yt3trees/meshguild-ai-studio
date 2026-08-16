using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ModelContextProtocol.AspNetCore;

namespace WorkAgents.Host.Mcp;

public static class McpServiceCollectionExtensions
{
    public static IServiceCollection AddWorkAgentsMcp(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<McpOptions>()
            .Bind(configuration.GetSection(McpOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<McpOptions>, McpOptionsValidator>();
        services.AddSingleton<McpRequestValidator>();
        services.AddSingleton<McpAuditLogger>();
        services.AddSingleton<McpResourceAccessPolicy>();
        services.AddSingleton<McpObservationTools>();

        var options = configuration.GetSection(McpOptions.SectionName).Get<McpOptions>() ?? new McpOptions();
        if (!options.Enabled)
        {
            return services;
        }

        var sessionMode = Enum.TryParse<HttpServerSessionMode>(options.SessionMode, ignoreCase: true, out var parsed)
            ? parsed
            : HttpServerSessionMode.StatefulForInitializeClients;

        services.AddMcpServer()
            .WithHttpTransport(transport => transport.SessionMode = sessionMode)
            .WithTools<McpDefinitionTools>()
            .WithTools<McpMissionTools>()
            .WithTools<McpObservationTools>()
            .WithTools<McpApprovalTools>()
            .WithResources<McpResourceCatalog>();

        return services;
    }

    public static WebApplication MapWorkAgentsMcp(this WebApplication app)
    {
        var options = app.Services.GetRequiredService<IOptions<McpOptions>>().Value;
        if (!options.Enabled)
        {
            return app;
        }

        app.MapMcp(options.EndpointPath);
        return app;
    }

    public static WebApplication UseWorkAgentsMcpSecurity(this WebApplication app)
    {
        var options = app.Services.GetRequiredService<IOptions<McpOptions>>().Value;
        if (!options.Enabled)
        {
            return app;
        }

        app.UseWhen(
            context => context.Request.Path.StartsWithSegments(options.EndpointPath),
            branch => branch.Use(async (context, next) =>
            {
                var issue = context.RequestServices.GetRequiredService<McpRequestValidator>().ValidateHttpRequest(context);
                if (issue is null)
                {
                    await next();
                    return;
                }

                context.Response.StatusCode = issue.StatusCode;
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsJsonAsync(new
                {
                    error = new { code = issue.Code, message = issue.Message },
                });
            }));

        return app;
    }
}
