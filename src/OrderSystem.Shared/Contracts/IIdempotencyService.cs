using System;

namespace OrderSystem.Shared.Contracts;

public interface IIdempotencyService
{
    /// <summary>
    /// Checks if a message key has already been processed.
    /// </summary>
    Task<bool> IsMessageProcessedAsync(string messageId);

    /// <summary>
    /// Marks a message key as processed with a set expiration time.
    /// </summary>
    Task MarkMessageAsProcessedAsync(string messageId);
}
