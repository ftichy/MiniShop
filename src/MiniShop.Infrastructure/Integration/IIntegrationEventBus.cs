using MiniShop.Contracts;

namespace MiniShop.Infrastructure.Integration;

/// <summary>
/// "Poštovní schránka ven": odešle integrační událost ostatním službám.
/// Volá ho jen <see cref="Outbox.OutboxProcessor"/> – aplikace sama publikuje přes IIntegrationEventPublisher (do outboxu).
/// </summary>
/// <remarks>
/// Implementace: <see cref="LoggingIntegrationEventBus"/> (jen log) a <see cref="RabbitMq.RabbitMqIntegrationEventBus"/> (krok 8).
/// Kterou použít, rozhoduje Messaging:Transport v appsettings.json. Zbytek aplikace o tom neví.
/// Když broker neběží, implementace vyhodí <see cref="IntegrationBusUnavailableException"/>.
/// </remarks>
public interface IIntegrationEventBus
{
    Task PublishAsync(IIntegrationEvent integrationEvent, CancellationToken cancellationToken = default);
}
