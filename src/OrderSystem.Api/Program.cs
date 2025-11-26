using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using OrderSystem.Api.Behaviors;
using OrderSystem.Api.Features.Orders;
using OrderSystem.Api.Infrastructure.BackgroundServices;
using OrderSystem.Api.Infrastructure.Persistence;
using OrderSystem.Api.Infrastructure.ServiceBus;
using Scalar.AspNetCore;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

// 1. Configure Serilog
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
    .AddService(serviceName: "order-system-api", serviceVersion: "1.0.0")
    .AddAttributes(new System.Collections.Generic.KeyValuePair<string, object>[]
    {
        new System.Collections.Generic.KeyValuePair<string, object>("service.instance.id", "order-system-api-instance")
    });

builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddAttributes(resourceBuilder.Build().Attributes))
    .WithTracing(tracing =>
    {
        tracing
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddEntityFrameworkCoreInstrumentation()
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
            .AddMeter("order-system-api-metrics")
            .AddAspNetCoreInstrumentation()
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

// Forward the logs to OpenTelemetry (structured logs)
builder.Logging.ClearProviders();
builder.Logging.AddConsole();

// Basic validation: ensure endpoint looks valid
if (!Uri.TryCreate(otlpEndpoint, UriKind.Absolute, out var _))
{
    Log.Warning("OTLP endpoint invalid: {Endpoint}", otlpEndpoint);
}
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));
builder.Services.AddHostedService<OutboxProcessor>();

// 2. Add Services
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// 3. Register Application Services
builder.Services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(Program).Assembly));
builder.Services.AddValidatorsFromAssembly(typeof(Program).Assembly);
builder.Services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));


// 4. Register Infrastructure
builder.Services.AddSingleton<IMessageBus, AzureServiceBusPublisher>();

var app = builder.Build();

// Apply EF Core migrations at startup (if any). Ensure the Sql Server is available before calling Migrate.
if (app.Environment.IsDevelopment())
{
    using (var scope = app.Services.CreateScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        try
        {
            db.Database.Migrate();
        }
        catch (Exception ex)
        {
            var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
            logger.LogError(ex, "Failed to migrate database on startup.");
            // Swallow exception in development so app can still start without crashing.
        }
    }
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    // app.UseSwagger();
    // app.UseSwaggerUI();
    // Generate the OpenAPI JSON (Required for Scalar to read)
    app.UseSwagger(options =>
    {
        options.RouteTemplate = "openapi/{documentName}.json";
    });

    // Replace SwaggerUI with Scalar
    app.MapScalarApiReference(options =>
    {
        options.WithTitle("Order System API")
               .WithTheme(ScalarTheme.Moon) // Options: BluePlanet, DeepSpace, Moon, Mars, None
               .WithDefaultHttpClient(ScalarTarget.CSharp, ScalarClient.HttpClient);
    });
}

app.UseHttpsRedirection();
app.UseSerilogRequestLogging();
app.UseHttpsRedirection();
app.UseAuthorization();

// Minimal API Endpoint for testing
app.MapPost("/api/orders", async (IMediator mediator, CreateOrderCommand command) =>
{
    try
    {
        var orderId = await mediator.Send(command);
        return Results.Created($"/api/orders/{orderId}", new { OrderId = orderId });
    }
    catch (ValidationException ex)
    {
        return Results.BadRequest(ex.Errors);
    }
});

app.Run();
