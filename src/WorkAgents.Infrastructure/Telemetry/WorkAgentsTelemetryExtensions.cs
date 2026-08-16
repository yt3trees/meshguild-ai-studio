using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace WorkAgents.Infrastructure.Telemetry;

/// <summary>
/// 共通の OpenTelemetry 配線(第3章「可観測性」, 5.14)。
/// Local: Aspire Dashboard(OTLP) または Console 出力。Azure: App Insights 系 OTLP。
/// <para>
/// 設定:
/// <list type="bullet">
/// <item><c>OTEL_EXPORTER_OTLP_ENDPOINT</c>(env/設定): 指定時 OTLPExporter を有効化(Aspire Dashboard 等)。</item>
/// <item><c>OTEL_CONSOLE_DISABLED</c>: true で ConsoleExporter を停止。</item>
/// </list>
/// OTLP 未指定時は Console 出力のみ。Dashboard がローカルで立っていれば env を設定するだけで切替可。
/// </summary>
public static class WorkAgentsTelemetryExtensions
{
    public static IServiceCollection AddWorkAgentsTelemetry(
        this IServiceCollection services,
        IConfiguration configuration,
        string serviceName)
    {
        var otlpEndpoint =
            configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]
            ?? Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT");
        var consoleDisabled = configuration.GetValue("OTEL_CONSOLE_DISABLED", false);

        services.AddOpenTelemetry()
            .ConfigureResource(r => r
                .AddService(serviceName: serviceName, serviceVersion: "0.0.1")
                .AddAttributes([new("deployment.environment", "local")]))
            .WithTracing(t =>
            {
                t.AddSource(WorkAgentsTelemetry.ActivitySourceName);
                t.AddAspNetCoreInstrumentation();

                // Runtime instrumentation は Metrics 系。Tracing には無い。
                // M8 で .WithMetrics(m => m.AddRuntimeInstrumentation()...) を追加。

                if (!string.IsNullOrEmpty(otlpEndpoint))
                {
                    t.AddOtlpExporter(o => o.Endpoint = new Uri(otlpEndpoint));
                }

                if (!consoleDisabled)
                {
                    t.AddConsoleExporter();
                }
            });

        return services;
    }
}