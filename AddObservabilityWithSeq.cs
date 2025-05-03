using Microsoft.AspNetCore.Builder;
using Serilog.Events;
using Serilog;
using Serilog.Enrichers.Span;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using OpenTelemetry.Exporter;

namespace Shared.Observability;

public static class ObservabilityExtensions
{
    public static void AddObservabilityWithSeq(
        this WebApplicationBuilder builder,
        string serviceName,
        Action<LoggerConfiguration>? modifySerilogConfig = null,
        Action<TracerProviderBuilder>? modifyTracingConfig = null)
    {
        // Configure Serilog with TraceId + SpanId enrichment
        var loggerConfig = new LoggerConfiguration()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .MinimumLevel.Override("System", LogEventLevel.Information)
            .Enrich.WithProperty("service", serviceName)
            .Enrich.WithSpan()
            .WriteTo.Console()
            .WriteTo.Seq("http://localhost:5342");

        // Modify Serilog configuration from the caller
        modifySerilogConfig?.Invoke(loggerConfig);

        Log.Logger = loggerConfig.CreateLogger();

        builder.Host.UseSerilog();

        // OpenTelemetry for distributed Activity tracking
        builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(serviceName))
            .WithTracing(tracing =>
            {
                tracing
                    .AddAspNetCoreInstrumentation(options =>
                    {
                        options.Filter = httpContext =>
                        {
                            var path = httpContext.Request.Path.Value;
                            return !(path.Contains("health") ||
                                     path.Contains("favicon") ||
                                     path.Contains(".js") ||
                                     path.Contains(".css") ||
                                     path.Contains("swagger") ||
                                     path.Contains("metrics") ||
                                     path.Contains("images")
                                     );
                        };
                    })
                    .AddHttpClientInstrumentation()
                    .AddOtlpExporter(option =>
                    {
                        option.Endpoint = new Uri("http://localhost:5341/ingest/otlp/v1/traces");
                        option.Protocol = OtlpExportProtocol.HttpProtobuf;
                    });

                // Modify tracing configuration from the caller
                modifyTracingConfig?.Invoke(tracing);
            });
    }
}
