using System;
using System.Threading;
using System.Threading.Tasks;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace OrderSystem.Worker.Infrastructure;

public class ServiceBusHealthCheck : IHealthCheck
{
    private readonly IConfiguration _configuration;

    public ServiceBusHealthCheck(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var connectionString = _configuration.GetConnectionString("ServiceBus");

            // 1. Create specific options JUST for this health check.
            // We do NOT use the shared Factory here because we want different rules.
            var probeOptions = new ServiceBusClientOptions
            {
                RetryOptions = new ServiceBusRetryOptions
                {
                    // 🛑 FAIL FAST SETTINGS
                    Mode = ServiceBusRetryMode.Fixed,
                    MaxRetries = 0, // Do not retry. The orchestrator (K8s) will check again later.
                    TryTimeout = TimeSpan.FromSeconds(3) // Give up after 3 seconds
                }
            };

            // Handle Emulator Connection (Same logic as Factory, but local)
            if (connectionString.Contains("UseDevelopmentEmulator=true"))
            {
                probeOptions.TransportType = ServiceBusTransportType.AmqpTcp;
            }

            // 2. Create the client
            await using var client = new ServiceBusClient(connectionString, probeOptions);
            var sender = client.CreateSender("orders-topic");

            // 3. Enforce a strict timeout using CancellationToken
            // Even if the SDK ignores the TryTimeout, this token ensures we return.
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

            // Force network call
            using var batch = await sender.CreateMessageBatchAsync(cts.Token);

            return HealthCheckResult.Healthy("Service Bus is reachable.");
        }
        catch (OperationCanceledException)
        {
            return HealthCheckResult.Unhealthy("Service Bus connection timed out (Fail Fast).");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy($"Service Bus connection failed: {ex.Message}");
        }
    }
}