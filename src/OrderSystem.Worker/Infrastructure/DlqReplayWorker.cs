using System;
using System.Threading;
using System.Threading.Tasks;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OrderSystem.Shared.Infrastructure;

namespace OrderSystem.Worker.Infrastructure;

public class DlqReplayWorker : BackgroundService
{
    private readonly ServiceBusClient _client;
    private readonly ILogger<DlqReplayWorker> _logger;
    private readonly string _topicName = "orders-topic";
    private readonly string _subscriptionName = "inventory-sub";

    public DlqReplayWorker(IConfiguration configuration, ILogger<DlqReplayWorker> logger)
    {
        _logger = logger;
        var connectionString = configuration.GetConnectionString("ServiceBus");
        _client = ServiceBusFactory.CreateClient(connectionString);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("DLQ Replay Worker started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessDlqMessages(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred during DLQ processing.");
            }

            // In production, run this every 5-10 minutes, not constantly.
            // await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken);
            // local testing
            await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
        }
    }

    private async Task ProcessDlqMessages(CancellationToken stoppingToken)
    {
        // 1. Connect specifically to the Dead Letter Queue (SubQueue)
        ServiceBusReceiver dlqReceiver = _client.CreateReceiver(_topicName, _subscriptionName, new ServiceBusReceiverOptions
        {
            SubQueue = SubQueue.DeadLetter // <--- MAGIC SAUCE
        });

        // 2. Create a Sender to resubmit messages back to the MAIN Topic
        ServiceBusSender mainTopicSender = _client.CreateSender(_topicName);

        // Fetch up to 10 messages
        var messages = await dlqReceiver.ReceiveMessagesAsync(10, TimeSpan.FromSeconds(5), cancellationToken: stoppingToken);

        foreach (var message in messages)
        {
            _logger.LogWarning("DLQ Inspector found message: {MessageId}. Reason: {Reason}",
                message.MessageId, message.DeadLetterReason);

            // --- ZOMBIE PROTECTION ---
            int replayCount = 0;
            if (message.ApplicationProperties.TryGetValue("Replay-Count", out var val) && val is int count)
            {
                replayCount = count;
            }

            if (replayCount >= 3)
            {
                _logger.LogError("Message {MessageId} is a ZOMBIE (Replayed {Count} times). Purging.", message.MessageId, replayCount);

                // We mark it complete in the DLQ, effectively deleting it forever.
                // In a real bank app, you would save this to a 'FailedAudit' database table before deleting.
                await dlqReceiver.CompleteMessageAsync(message, stoppingToken);
                continue;
            }

            // --- REPLAY LOGIC ---
            // We must create a NEW message because ServiceBus messages are immutable once sent.
            var clonedMessage = new ServiceBusMessage(message.Body)
            {
                MessageId = message.MessageId, // Keep original ID for traceability
                CorrelationId = message.CorrelationId,
                Subject = message.Subject
            };

            // Increment Replay Counter
            clonedMessage.ApplicationProperties["Replay-Count"] = replayCount + 1;

            _logger.LogInformation("Replaying message {MessageId} (Attempt #{Attempt})", message.MessageId, replayCount + 1);

            // 1. Send to Main Queue
            await mainTopicSender.SendMessageAsync(clonedMessage, stoppingToken);

            // 2. Remove from DLQ
            await dlqReceiver.CompleteMessageAsync(message, stoppingToken);
        }

        await dlqReceiver.DisposeAsync();
        await mainTopicSender.DisposeAsync();
    }
}