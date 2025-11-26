using OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using OrderSystem.Shared.Contracts;
using OrderSystem.Worker;
using OrderSystem.Worker.Infrastructure;
using OrderSystem.Worker.Services;
using Serilog;

// 1. We switch from "Host.CreateDefaultBuilder" to "WebApplication.CreateBuilder" 
// This allows the Worker to serve an HTTP Health Endpoint easily.
var builder = WebApplication.CreateBuilder(args);

// Configure Serilog
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .CreateLogger();

builder.Host.UseSerilog();

// Read OTLP configuration from environment variables or fallback defaults.
// Best practice: set OTEL_EXPORTER_OTLP_ENDPOINT and optional OTEL_EXPORTER_OTLP_PROTOCOL
var otlpEndpoint = Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT") ?? "http://localhost:4317";
var otlpProtocol = Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_PROTOCOL") ?? "grpc";
var otlpApiKey = Environment.GetEnvironmentVariable("OTEL_API_KEY") ?? Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_API_KEY");

// Resource info: create a single, consistent Resource with service.name, service.version and service.instance.id
var resourceBuilder = ResourceBuilder.CreateDefault()
    .AddService(serviceName: "order-system-worker", serviceVersion: "1.0.0")
    .AddAttributes(new System.Collections.Generic.KeyValuePair<string, object>[]
    {
        new System.Collections.Generic.KeyValuePair<string, object>("service.instance.id", "order-system-worker-instance")
    });

builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddAttributes(resourceBuilder.Build().Attributes))
    .WithTracing(tracing =>
    {
        tracing
            // We don't use AspNetCoreInstrumentation here because it's a worker
            .AddHttpClientInstrumentation()
            // 👇 CRITICAL: This links the Consumer trace to the Producer trace
            .AddSource("Azure.Messaging.ServiceBus")
            .AddOtlpExporter(otlpOptions =>
            {
                otlpOptions.Endpoint = new Uri(otlpEndpoint);
                otlpOptions.Protocol = otlpProtocol.Equals("http/protobuf", StringComparison.OrdinalIgnoreCase)
                    ? OtlpExportProtocol.HttpProtobuf
                    : OtlpExportProtocol.Grpc;

                if (!string.IsNullOrWhiteSpace(otlpApiKey))
                {
                    // attach custom header expected by Aspire
                    otlpOptions.Headers = $"x-otlp-api-key={otlpApiKey}";
                }

                // Timeout and batch settings for resilience & perf
                otlpOptions.TimeoutMilliseconds = 10000; // 10s
            });
    })
    .WithMetrics(metrics =>
    {
        metrics
            .AddMeter("order-system-worker-metrics")
            .AddHttpClientInstrumentation()
            .AddRuntimeInstrumentation()
            .AddOtlpExporter(otlpOptions =>
            {
                otlpOptions.Endpoint = new Uri(otlpEndpoint);
                otlpOptions.Protocol = otlpProtocol.Equals("http/protobuf", StringComparison.OrdinalIgnoreCase)
                    ? OtlpExportProtocol.HttpProtobuf
                    : OtlpExportProtocol.Grpc;
                if (!string.IsNullOrWhiteSpace(otlpApiKey))
                    otlpOptions.Headers = $"x-otlp-api-key={otlpApiKey}";
            });
    });

// 2. Register Services
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSingleton<IIdempotencyService, IdempotencyService>();
builder.Services.AddSingleton<OrderCreatedConsumer>();
builder.Services.AddHostedService<Worker>();
// The DLQ Doctor (New)
builder.Services.AddHostedService<DlqReplayWorker>();

// 3. Register Health Checks
builder.Services.AddHealthChecks()
    .AddCheck<ServiceBusHealthCheck>("service_bus_check");

var app = builder.Build();

// 4. Map the Health Endpoint
app.MapHealthChecks("/health");

// 5. Run the App
await app.RunAsync();