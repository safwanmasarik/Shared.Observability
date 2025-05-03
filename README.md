# Shared.Observability
Add Observability into all our microservices with Serilog &amp; Seq for our application's distributed microservices logging &amp; tracing. 
Guide below only for local development to increase productivity e.g. quickly identify errors.

Here I want to share how to add Observability into all our microservices with Serilog & Seq for our application logging & distributed tracing.
Guide below only for local development to increase productivity e.g. quickly identify errors.

Preview of Seq dashboard:
![image](https://github.com/user-attachments/assets/578c9f1a-f8b7-44b1-8cbd-0d7db3ba7477)

Guide:
1. Run Seq container, example via `docker-compose.yml` file, run command via terminal `docker compose up -d`.
```yaml	
seq:
    image: datalust/seq:latest
    ports:
      - "5341:80"      # Web UI
      - "5342:5341"    # Ingest logs
    environment:
      - ACCEPT_EULA=Y
```

2.  Create a Class library, `Shared.Observability.csproj`.
	
3. Add package to it.
```cmd
dotnet add package OpenTelemetry.Exporter.OpenTelemetryProtocol
dotnet add package OpenTelemetry.Extensions.Hosting
dotnet add package OpenTelemetry.Instrumentation.AspNetCore
dotnet add package OpenTelemetry.Instrumentation.Http
dotnet add package Serilog.AspNetCore
dotnet add package Serilog.Enrichers.Span
dotnet add package Serilog.Sinks.Seq
```
```xml
<PackageReference Include="OpenTelemetry.Exporter.OpenTelemetryProtocol" Version="1.12.0" />
<PackageReference Include="OpenTelemetry.Extensions.Hosting" Version="1.12.0" />
<PackageReference Include="OpenTelemetry.Instrumentation.AspNetCore" Version="1.11.1" />
<PackageReference Include="OpenTelemetry.Instrumentation.Http" Version="1.11.1" />
<PackageReference Include="Serilog.AspNetCore" Version="9.0.0" />
<PackageReference Include="Serilog.Enrichers.Span" Version="3.1.0" />
<PackageReference Include="Serilog.Sinks.Seq" Version="9.0.0" />
```	
	
4. Add a file `AddObservabilityWithSeq.cs` with content.
```cs
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
    public static void AddObservabilityWithSeq(this WebApplicationBuilder builder, string serviceName, Action<LoggerConfiguration>? addSerilogConfig = null)
    {
	// Configure Serilog with TraceId + SpanId enrichment
	var loggerConfig = new LoggerConfiguration()
	    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
	    .MinimumLevel.Override("System", LogEventLevel.Information)
	    .Enrich.WithProperty("service", serviceName)
	    .Enrich.WithSpan()
	    .WriteTo.Console()
	    .WriteTo.Seq("http://localhost:5342");

	// Apply additional Serilog configuration
	addSerilogConfig?.Invoke(loggerConfig);

	Log.Logger = loggerConfig.CreateLogger();

	builder.Host.UseSerilog();

	// Optional: OpenTelemetry for distributed Activity tracking
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
		    .AddSource("MassTransit")
		    .AddOtlpExporter(option =>
		    {
			option.Endpoint = new Uri("http://localhost:5341/ingest/otlp/v1/traces");
			option.Protocol = OtlpExportProtocol.HttpProtobuf;
		    });
	    });
    }
}
```

5. All hosted microservices, add project reference to `Shared.Observability.csproj`.
```xml
<ProjectReference Include="..\..\..\Common\Shared.Observability\Shared.Observability.csproj" />
```

6. In all hosted projects that you want to observe e.g. AuthService.API, eShop.Web, consume the Observability method extension into the builder service in the `Program.cs` file.
```cs
// Add observability
builder.AddObservabilityWithSeq(serviceName: "Api.Auth");
```

7. Happy coding!

