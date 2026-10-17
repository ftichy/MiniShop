using System.Text.Json;
using Microsoft.Extensions.Logging;
using MiniShop.Contracts;

namespace MiniShop.Infrastructure.Integration;

/// <summary>
/// Zástupce message brokeru (krok 7): událost jen zapíše do logu i s JSONem, který by odešel ven.
/// Používá se, když Messaging:Transport = "Log" – aplikace pak běží i bez RabbitMQ (krok 8).
/// </summary>
public sealed class LoggingIntegrationEventBus(ILogger<LoggingIntegrationEventBus> logger) : IIntegrationEventBus
{
    public Task PublishAsync(IIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        logger.LogInformation("[Integrační událost] {Type} {EventId} odeslána ven: {Payload}",
            integrationEvent.GetType().Name,
            integrationEvent.EventId,
            JsonSerializer.Serialize(integrationEvent, integrationEvent.GetType()));
        return Task.CompletedTask;
    }
}
