using System;
using System.Text.Json;
using System.Threading.Tasks;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using OrderSystem.Shared.Contracts;

namespace OrderSystem.Worker.Infrastructure;

public class OrderCreatedConsumer : IAsyncDisposable
{
    private readonly ServiceBusProcessor _processor;
    private readonly ILogger<OrderCreatedConsumer> _logger;
    private readonly IIdempotencyService _idempotencyService;

    public OrderCreatedConsumer(IConfiguration configuration,
        ILogger<OrderCreatedConsumer> logger,
        IIdempotencyService idempotencyService)
    {
        _logger = logger;
        _idempotencyService = idempotencyService;

        var connectionString = configuration.GetConnectionString("ServiceBus");
        var topicName = "orders-topic";
        var subscriptionName = "inventory-sub"; // Ensure this exists in Azure

        var client = new ServiceBusClient(connectionString);

        // Create a processor (handles message pump automatically)
        _processor = client.CreateProcessor(topicName, subscriptionName, new ServiceBusProcessorOptions
        {
            AutoCompleteMessages = false, // We want explicit control over Ack/Nack
            MaxConcurrentCalls = 1
        });

        _processor.ProcessMessageAsync += MessageHandler;
        _processor.ProcessErrorAsync += ErrorHandler;
    }

    public async Task StartProcessingAsync()
    {
        _logger.LogInformation("Starting Service Bus Processor...");
        await _processor.StartProcessingAsync();
    }

    public async Task StopProcessingAsync()
    {
        _logger.LogInformation("Stopping Service Bus Processor...");
        await _processor.StopProcessingAsync();
    }

    // public async Task StartProcessingAsync() => await _processor.StartProcessingAsync();
    // public async Task StopProcessingAsync() => await _processor.StopProcessingAsync();

    private async Task MessageHandler(ProcessMessageEventArgs args)
    {
        string messageId = args.Message.MessageId;

        try
        {
            // --- 1. IDEMPOTENCY CHECK ---
            if (await _idempotencyService.IsMessageProcessedAsync(messageId))
            {
                _logger.LogWarning("Duplicate Message detected: {MessageId}. Skipping processing.", messageId);

                // IMPORTANT: We must still "Complete" the message to remove it from the Service Bus.
                await args.CompleteMessageAsync(args.Message);
                return;
            }

            string body = args.Message.Body.ToString();

            // Inside MessageHandler
            // Temporary: Force failure
            if (body.Contains("fail-me"))
            {
                throw new Exception("Simulated Crash!");
            }

            _logger.LogInformation("Received Message: {MessageId}. Correlation: {CorrelationId}", args.Message.MessageId, args.Message.CorrelationId);

            // Deserialize
            var orderEvent = JsonSerializer.Deserialize<OrderCreatedEvent>(body);

            if (orderEvent != null)
            {
                // --- BUSINESS LOGIC HERE ---
                _logger.LogInformation("Processing Order {OrderId}. Reserving stock for Customer {CustomerId}...",
                    orderEvent.OrderId, orderEvent.CustomerId);

                await Task.Delay(100); // Simulating work

                // --- MARK AS PROCESSED ---
                // We mark it BEFORE completing the message. 
                // Ideally, this operation and your DB transaction should be atomic (Outbox Pattern),
                // but for this level, this is the standard robust approach.
                await _idempotencyService.MarkMessageAsProcessedAsync(messageId);

                // Complete the message (remove from Service Bus)
                await args.CompleteMessageAsync(args.Message);
                _logger.LogInformation("Message {MessageId} processed and completed.", args.Message.MessageId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing message {MessageId}", args.Message.MessageId);

            // Abandon message (puts it back in queue for retry or dead-letter)
            await args.AbandonMessageAsync(args.Message);
        }
    }

    private Task ErrorHandler(ProcessErrorEventArgs args)
    {
        _logger.LogError(args.Exception, "Message handler encountered an exception. Source: {ErrorSource}", args.ErrorSource);
        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        await _processor.DisposeAsync();
    }
}