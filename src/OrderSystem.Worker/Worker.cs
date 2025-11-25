using OrderSystem.Worker.Infrastructure;

namespace OrderSystem.Worker;

public class Worker : BackgroundService
{
    private readonly OrderCreatedConsumer _consumer;

    public Worker(OrderCreatedConsumer consumer)
    {
        _consumer = consumer;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Start the Service Bus processor
        await _consumer.StartProcessingAsync();

        // Keep the worker alive until cancelled
        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(1000, stoppingToken);
        }

        await _consumer.StopProcessingAsync();
    }
}