using OrderSystem.Worker;
using OrderSystem.Worker.Infrastructure;
using Serilog;

var host = Host.CreateDefaultBuilder(args)
    .UseSerilog((context, services, configuration) => configuration
        .ReadFrom.Configuration(context.Configuration)
        .Enrich.FromLogContext()
        .WriteTo.Console())
    .ConfigureServices((hostContext, services) =>
    {
        services.AddSingleton<OrderCreatedConsumer>();
        services.AddHostedService<Worker>();
    })
    .Build();

await host.RunAsync();