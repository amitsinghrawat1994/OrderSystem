using OrderSystem.Shared.Contracts;
using OrderSystem.Worker;
using OrderSystem.Worker.Infrastructure;
using OrderSystem.Worker.Services;
using Serilog;
using Microsoft.Extensions.DependencyInjection;

var host = Host.CreateDefaultBuilder(args)
    .UseSerilog((context, services, configuration) => configuration
        .ReadFrom.Configuration(context.Configuration)
        .Enrich.FromLogContext()
        .WriteTo.Console())
    .ConfigureServices((hostContext, services) =>
    {
        // 1. Add Distributed Memory Cache (Stores data in RAM, looks like Redis)
        services.AddDistributedMemoryCache();

        // 2. Register Idempotency Service
        services.AddSingleton<IIdempotencyService, IdempotencyService>();

        // 3. Register Consumer and Worker
        services.AddSingleton<OrderCreatedConsumer>();
        services.AddHostedService<Worker>();

        // The DLQ Doctor (New)
        services.AddHostedService<DlqReplayWorker>();
    })
    .Build();

await host.RunAsync();
