namespace OrderSystem.Api.Infrastructure.Persistence;

public class OutboxMessage
{
    public Guid Id { get; set; }
    public string Type { get; set; } = string.Empty; // e.g., "OrderCreatedEvent"
    public string Content { get; set; } = string.Empty; // The JSON Payload
    public DateTime OccurredOnUtc { get; set; }
    public DateTime? ProcessedOnUtc { get; set; } // Null = Not sent yet
    public string? Error { get; set; }
}
