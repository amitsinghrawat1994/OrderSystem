using System;

namespace OrderSystem.Shared.Contracts;

public record OrderCreatedEvent(
    Guid OrderId,
    string CustomerId,
    decimal TotalAmount,
    DateTime CreatedAt
);
