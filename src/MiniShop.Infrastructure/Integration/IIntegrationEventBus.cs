using MiniShop.Contracts;

namespace MiniShop.Infrastructure.Integration;

/// <summary>
/// "Poštovní schránka ven": odešle integrační událost ostatním službám.
/// Volá ho jen <see cref="Outbox.OutboxProcessor"/> – aplikace sama publikuje přes IIntegrationEventPublisher (do outboxu).
/// </summary>
/// <remarks>
/// Zatím jen <see cref="LoggingIntegrationEventBus"/>. V kroku 8 přibude implementace pro RabbitMQ
/// a nic jiného se měnit nebude.
/// </remarks>
public interface IIntegrationEventBus
{
    Task PublishAsync(IIntegrationEvent integrationEvent, CancellationToken cancellationToken = default);
}
