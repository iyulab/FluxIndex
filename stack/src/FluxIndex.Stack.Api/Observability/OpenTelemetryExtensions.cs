using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace FluxIndex.Stack.Api.Observability;

/// <summary>
/// OpenTelemetry configuration extensions for FluxIndex Stack.
/// Configures metrics, tracing, and exporters for Prometheus and Jaeger.
/// </summary>
public static class OpenTelemetryExtensions
{
    /// <summary>
    /// Adds OpenTelemetry observability to the service collection.
    /// Configures metrics with Prometheus exporter and tracing with OTLP exporter.
    /// </summary>
    public static IServiceCollection AddFluxIndexObservability(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var serviceName = configuration.GetValue<string>("OpenTelemetry:ServiceName") ?? "FluxIndex.Stack";
        var serviceVersion = configuration.GetValue<string>("OpenTelemetry:ServiceVersion") ?? "1.0.0";
        var otlpEndpoint = configuration.GetValue<string>("OpenTelemetry:OtlpEndpoint");

        // Register RagMetrics as singleton
        services.AddSingleton<RagMetrics>();

        var otel = services.AddOpenTelemetry();

        // Configure resource
        otel.ConfigureResource(resource => resource
            .AddService(
                serviceName: serviceName,
                serviceVersion: serviceVersion,
                serviceInstanceId: Environment.MachineName)
            .AddAttributes(new Dictionary<string, object>
            {
                ["deployment.environment"] = configuration.GetValue<string>("ASPNETCORE_ENVIRONMENT") ?? "Development",
                ["service.namespace"] = "FluxIndex"
            }));

        // Configure metrics
        otel.WithMetrics(metrics =>
        {
            // ASP.NET Core instrumentation
            metrics.AddAspNetCoreInstrumentation();

            // HTTP client instrumentation
            metrics.AddHttpClientInstrumentation();

            // Built-in .NET meters
            metrics.AddMeter("Microsoft.AspNetCore.Hosting");
            metrics.AddMeter("Microsoft.AspNetCore.Server.Kestrel");
            metrics.AddMeter("System.Net.Http");
            metrics.AddMeter("System.Net.NameResolution");

            // Custom RAG metrics
            metrics.AddMeter(RagMetrics.MeterName);

            // Prometheus exporter (exposes /metrics endpoint)
            metrics.AddPrometheusExporter();

            // Optional: OTLP exporter for metrics
            if (!string.IsNullOrEmpty(otlpEndpoint))
            {
                metrics.AddOtlpExporter(opts =>
                {
                    opts.Endpoint = new Uri(otlpEndpoint);
                });
            }
        });

        // Configure tracing
        otel.WithTracing(tracing =>
        {
            // ASP.NET Core instrumentation
            tracing.AddAspNetCoreInstrumentation(opts =>
            {
                opts.RecordException = true;
                opts.Filter = ctx =>
                {
                    // Skip health check and metrics endpoints
                    var path = ctx.Request.Path.Value ?? "";
                    return !path.StartsWith("/health", StringComparison.OrdinalIgnoreCase)
                        && !path.StartsWith("/metrics", StringComparison.OrdinalIgnoreCase)
                        && !path.StartsWith("/ready", StringComparison.OrdinalIgnoreCase);
                };
            });

            // HTTP client instrumentation
            tracing.AddHttpClientInstrumentation(opts =>
            {
                opts.RecordException = true;
            });

            // Entity Framework Core instrumentation
            tracing.AddEntityFrameworkCoreInstrumentation(opts =>
            {
                opts.SetDbStatementForText = true;
            });

            // Custom RAG activity source
            tracing.AddSource(RagMetrics.ActivitySourceName);

            // OTLP exporter (for Jaeger)
            if (!string.IsNullOrEmpty(otlpEndpoint))
            {
                tracing.AddOtlpExporter(opts =>
                {
                    opts.Endpoint = new Uri(otlpEndpoint);
                });
            }
        });

        return services;
    }

    /// <summary>
    /// Maps the Prometheus metrics scraping endpoint.
    /// </summary>
    public static IApplicationBuilder UseFluxIndexObservability(this IApplicationBuilder app)
    {
        // Map Prometheus scraping endpoint at /metrics
        app.UseOpenTelemetryPrometheusScrapingEndpoint();

        return app;
    }
}
