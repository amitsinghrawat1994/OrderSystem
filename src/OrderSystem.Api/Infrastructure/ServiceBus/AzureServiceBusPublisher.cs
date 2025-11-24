using System;
using System.Text.Json;
using Azure.Messaging.ServiceBus;

namespace OrderSystem.Api.Infrastructure.ServiceBus;

public class AzureServiceBusPublisher : IMessageBus, IAsyncDisposable
{
    private readonly ServiceBusClient _client;
    // private readonly IConfiguration _configuration;
    private readonly ILogger<AzureServiceBusPublisher> _logger;

    public AzureServiceBusPublisher(IConfiguration configuration, ILogger<AzureServiceBusPublisher> logger)
    {
        this._logger = logger;
        // this._configuration = configuration;
        var connectionString = configuration.GetConnectionString("ServiceBus");

        var options = new ServiceBusClientOptions()
        {
            RetryOptions = new ServiceBusRetryOptions()
            {
                Mode = ServiceBusRetryMode.Exponential,
                MaxRetries = 3,
                Delay = TimeSpan.FromMilliseconds(800),
                MaxDelay = TimeSpan.FromSeconds(10)
            }
        };

        // DETECT LOCAL EMULATOR: 
        // If the connection string contains "UseDevelopmentEmulator=true", we assume local usage.
        if (connectionString.Contains("UseDevelopmentEmulator=true"))
        {
            // WARNING: Only use this for local development!
            options.TransportType = ServiceBusTransportType.AmqpTcp;
            //options.AmqpWebSocketsEventSource = null; // Sometimes required for older SDKs

            // Currently, the .NET SDK doesn't expose a simple "IgnoreCertificate" property 
            // directly on ServiceBusClientOptions for AMQP.
            // The "UseDevelopmentEmulator=true" flag in the connection string 
            // handles most of this logic in the latest Azure.Messaging.ServiceBus package (v7.17+).
        }

        _client = new ServiceBusClient(connectionString, options);
    }

    public async Task PublishAsync<T>(T message, string topicName, CancellationToken cancellationToken = default)
    {
        ServiceBusSender sender = _client.CreateSender(topicName);

        try
        {
            string jsonMessage = JsonSerializer.Serialize(message);
            var busMessage = new ServiceBusMessage(jsonMessage)
            {
                MessageId = Guid.NewGuid().ToString(),
                CorrelationId = Guid.NewGuid().ToString(), // In real apps, grab this from HttpContext
                Subject = typeof(T).Name
            };

            _logger.LogInformation("Publishing message {MessageType} to topic {TopicName}", typeof(T).Name, topicName);

            await sender.SendMessageAsync(busMessage, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error publishing message to topic {TopicName}", topicName);
            throw;
        }
        finally
        {
            await sender.DisposeAsync();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _client.DisposeAsync();
    }
}
