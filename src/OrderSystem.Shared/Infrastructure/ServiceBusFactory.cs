using Azure.Messaging.ServiceBus;

namespace OrderSystem.Shared.Infrastructure;

public static class ServiceBusFactory
{
    public static ServiceBusClient CreateClient(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ArgumentNullException(nameof(connectionString), "Service Bus connection string cannot be null or empty.");
        }

        var options = new ServiceBusClientOptions
        {
            RetryOptions = new ServiceBusRetryOptions
            {
                Mode = ServiceBusRetryMode.Exponential,
                MaxRetries = 3,
                Delay = TimeSpan.FromMilliseconds(800),
                MaxDelay = TimeSpan.FromSeconds(10)
            }
        };

        // If using the Local Emulator, force AMQP over TCP
        if (connectionString.Contains("UseDevelopmentEmulator=true"))
        {
            options.TransportType = ServiceBusTransportType.AmqpTcp;
        }

        return new ServiceBusClient(connectionString, options);
    }
}