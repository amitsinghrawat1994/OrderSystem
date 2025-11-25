using Microsoft.Extensions.Caching.Distributed;
using OrderSystem.Shared.Contracts;

namespace OrderSystem.Worker.Services;

public class IdempotencyService : IIdempotencyService
{
    private readonly IDistributedCache _cache;

    public IdempotencyService(IDistributedCache cache)
    {
        _cache = cache;
    }

    public async Task<bool> IsMessageProcessedAsync(string messageId)
    {
        string key = $"msg_processed_{messageId}";
        var value = await _cache.GetStringAsync(key);

        return !string.IsNullOrEmpty(value);
    }

    public async Task MarkMessageAsProcessedAsync(string messageId)
    {
        string key = $"msg_processed_{messageId}";

        // We keep the record for 24 hours. 
        // After that, we assume the risk of duplication is zero (or handled by business logs).
        var options = new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(24)
        };

        await _cache.SetStringAsync(key, "PROCESSED", options);
    }
}