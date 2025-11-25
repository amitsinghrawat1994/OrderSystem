using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
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
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .CreateLogger();

builder.Host.UseSerilog();

// Configure EF Core to use SQL Server. The connection string is read from appsettings (DefaultConnection).
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
