using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using OrderSystem.Api.Infrastructure.Persistence;
using OrderSystem.Api.Infrastructure.ServiceBus;
using OrderSystem.Shared.Contracts;

namespace OrderSystem.Api.Infrastructure.BackgroundServices;

public class OutboxProcessor : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<OutboxProcessor> _logger;
    private readonly IMessageBus _messageBus;
    private readonly IConfiguration _configuration;
    private readonly bool _simulateServiceBusTimeout;
    private readonly int _maxPublishAttempts;
    private readonly int _publishRetryDelayMs;

    public OutboxProcessor(
        IServiceScopeFactory scopeFactory,
        ILogger<OutboxProcessor> logger,
        IMessageBus messageBus,
        IConfiguration configuration)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _messageBus = messageBus;
        _configuration = configuration;

        _simulateServiceBusTimeout = _configuration.GetValue<bool>("Outbox:SimulateServiceBusTimeout", false);
        _maxPublishAttempts = _configuration.GetValue<int>("Outbox:MaxPublishAttempts", 3);
        _publishRetryDelayMs = _configuration.GetValue<int>("Outbox:PublishRetryDelayMs", 200);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Outbox Processor started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessOutboxMessages(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing outbox.");
            }

            // Poll every 5 seconds (Adjust based on requirements)
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        }
    }

    private async Task ProcessOutboxMessages(CancellationToken stoppingToken)
    {
        // 1. Create a scope (because DbContext is Scoped, but BackgroundService is Singleton)
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // 2. Fetch unprocessed messages (Max 20 at a time to prevent memory bloat)
        var messages = await dbContext.OutboxMessages
            .Where(m => m.ProcessedOnUtc == null)
            .OrderBy(m => m.OccurredOnUtc)
            .Take(20)
            .ToListAsync(stoppingToken);

        if (!messages.Any()) return;

        foreach (var message in messages)
        {
            try
            {
                // 3. Deserialize based on Type
                // In a real system, you might use a Type Registry. 
                // Here we assume it's OrderCreatedEvent for simplicity.
                if (message.Type == nameof(OrderCreatedEvent))
                {
                    var eventData = JsonSerializer.Deserialize<OrderCreatedEvent>(message.Content);
                    if (eventData != null)
                    {
                        // If the Total Amount is exactly 999, simulate a network crash.
                        if (eventData.TotalAmount == 999 && _simulateServiceBusTimeout)
                        {
                            // Simulate a transient network time out / Service Bus failure used for local testing
                            throw new Exception("Simulated Azure Service Bus Timeout!");
                        }

                        // 4. Publish to Azure Service Bus with a small retry loop for transient errors
                        var attempt = 0;
                        var published = false;
                        Exception? lastEx = null;

                        while (!published && attempt < _maxPublishAttempts)
                        {
                            attempt++;
                            try
                            {
                                await _messageBus.PublishAsync(eventData, "orders-topic", stoppingToken);
                                published = true;
                            }
                            catch (Exception ex)
                            {
                                lastEx = ex;
                                _logger.LogWarning(ex, "Attempt {Attempt} failed publishing message {MessageId}", attempt, message.Id);
                                if (attempt < _maxPublishAttempts)
                                {
                                    var delay = _publishRetryDelayMs * Math.Pow(2, attempt - 1);
                                    await Task.Delay(TimeSpan.FromMilliseconds(delay), stoppingToken);
                                }
                            }
                        }

                        if (!published && lastEx is not null)
                        {
                            throw lastEx; // handled in outer catch for this message
                        }
                    }
                }

                // 5. Mark as Processed
                message.ProcessedOnUtc = DateTime.UtcNow;
                _logger.LogInformation("Published Outbox Message {MessageId} to Service Bus.", message.Id);
            }
            catch (Exception ex)
            {
                message.Error = ex.Message;
                _logger.LogError(ex, "Failed to publish message {MessageId}", message.Id);
                // We typically DON'T throw here, so we can process other messages. 
                // The failed one will stay in DB with an Error string.
            }
        }

        // 6. Save updates to DB
        await dbContext.SaveChangesAsync(stoppingToken);
    }
}