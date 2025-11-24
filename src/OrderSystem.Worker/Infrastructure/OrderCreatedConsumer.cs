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

    public OrderCreatedConsumer(IConfiguration configuration, ILogger<OrderCreatedConsumer> logger)
    {
        _logger = logger;
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

    private async Task MessageHandler(ProcessMessageEventArgs args)
    {
        try
        {
            string body = args.Message.Body.ToString();
            _logger.LogInformation("Received Message: {MessageId}. Correlation: {CorrelationId}", args.Message.MessageId, args.Message.CorrelationId);

            // Deserialize
            var orderEvent = JsonSerializer.Deserialize<OrderCreatedEvent>(body);

            if (orderEvent != null)
            {
                // --- BUSINESS LOGIC HERE ---
                _logger.LogInformation("Processing Order {OrderId}. Reserving stock for Customer {CustomerId}...",
                    orderEvent.OrderId, orderEvent.CustomerId);

                await Task.Delay(100); // Simulating work
                // ---------------------------

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