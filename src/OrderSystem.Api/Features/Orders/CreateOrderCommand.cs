using System.Text.Json;
using MediatR;
using OrderSystem.Api.Infrastructure.Persistence;
using OrderSystem.Api.Infrastructure.ServiceBus;
using OrderSystem.Shared.Contracts;

namespace OrderSystem.Api.Features.Orders;

// Command Record
public record CreateOrderCommand(string CustomerId, decimal TotalAmount, List<string> Items) : IRequest<Guid>;

// Handler
public class CreateOrderHandler : IRequestHandler<CreateOrderCommand, Guid>
{
    private readonly AppDbContext _dbContext;
    private readonly IMessageBus _messageBus;
    private readonly ILogger<CreateOrderHandler> _logger;
    private const string TopicName = "orders-topic"; // Ensure this exists in Azure

    public CreateOrderHandler(AppDbContext dbContext, IMessageBus messageBus, ILogger<CreateOrderHandler> logger)
    {
        _dbContext = dbContext;
        _messageBus = messageBus;
        _logger = logger;
    }

    public async Task<Guid> Handle(CreateOrderCommand request, CancellationToken cancellationToken)
    {
        // 1. Simulate Database Logic
        var orderId = Guid.NewGuid();
        _logger.LogInformation($"Order {orderId} created in database for Customer {request.CustomerId}");

        // 2. Map to Event
        var integrationEvent = new OrderCreatedEvent(
            OrderId: orderId,
            CustomerId: request.CustomerId,
            TotalAmount: request.TotalAmount,
            CreatedAt: DateTime.UtcNow
        );

        var outboxMessage = new OutboxMessage
        {
            Id = Guid.NewGuid(),
            OccurredOnUtc = DateTime.UtcNow,
            Type = typeof(OrderCreatedEvent).Name,
            Content = JsonSerializer.Serialize(integrationEvent)
        };

        _dbContext.Orders.Add(integrationEvent);
        _dbContext.OutboxMessages.Add(outboxMessage);

        await _dbContext.SaveChangesAsync(cancellationToken);

        // // 3. Publish to Azure Service Bus
        // await _messageBus.PublishAsync(integrationEvent, TopicName, cancellationToken);

        return orderId;
    }
}