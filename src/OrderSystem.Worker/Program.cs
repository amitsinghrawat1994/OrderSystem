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
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .CreateLogger();

builder.Host.UseSerilog();

builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService("order-system-worker"))
    .WithTracing(tracing =>
    {
        tracing
            // We don't use AspNetCoreInstrumentation here because it's a worker
            .AddHttpClientInstrumentation()
            // 👇 CRITICAL: This links the Consumer trace to the Producer trace
            .AddSource("Azure.Messaging.ServiceBus")
            .AddOtlpExporter();
    })
    .WithMetrics(metrics =>
    {
        metrics
            .AddHttpClientInstrumentation()
            .AddRuntimeInstrumentation()
            .AddOtlpExporter();
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