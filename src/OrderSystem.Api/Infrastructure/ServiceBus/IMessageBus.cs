using System;

namespace OrderSystem.Api.Infrastructure.ServiceBus;

public interface IMessageBus
{
    Task PublishAsync<T>(T message, string topicName, CancellationToken cancellationToken = default);
}
