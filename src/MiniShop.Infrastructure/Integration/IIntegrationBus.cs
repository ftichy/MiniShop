using MiniShop.Contracts;

namespace MiniShop.Infrastructure.Integration;

/// <summary>
/// "Poštovní schránka ven": doručí integrační zprávu (událost nebo příkaz) jejím příjemcům.
/// Volá ho jen <see cref="Outbox.OutboxProcessor"/> – aplikace sama posílá přes IIntegrationMessagePublisher (do outboxu).
/// </summary>
/// <remarks>
/// Implementace: <see cref="InProcessIntegrationBus"/> (handlery přímo v procesu, bez brokeru) a
/// <see cref="RabbitMq.RabbitMqIntegrationBus"/> (krok 8). Kterou použít, rozhoduje Messaging:Transport v appsettings.json.
/// Když broker neběží, implementace vyhodí <see cref="IntegrationBusUnavailableException"/>.
/// </remarks>
public interface IIntegrationBus
{
    Task PublishAsync(IIntegrationMessage message, CancellationToken cancellationToken = default);
}
