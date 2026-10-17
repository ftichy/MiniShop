using Microsoft.Extensions.Logging;
using MiniShop.Contracts;
using MiniShop.Infrastructure.Inbox;

namespace MiniShop.Infrastructure.Integration;

/// <summary>
/// Doručí integrační zprávu handlerům JEDNOHO modulu (krok 9) – obdoba DomainEventDispatcher.
/// Každý handler běží přes <see cref="InboxGuard"/>, takže opakované doručení (retry z outboxu nebo z RabbitMQ) ho nespustí znovu.
/// </summary>
/// <remarks>
/// Scoped – handlery sdílejí DbContext scope. Když jeden handler selže, ostatní stejně proběhnou;
/// chyba se pak vyhodí, aby zprávu doručil znovu ten, kdo ji přinesl (outbox worker nebo RabbitMQ).
/// </remarks>
public sealed class IntegrationMessageDispatcher(
    IServiceProvider serviceProvider,
    IntegrationMessageRouting routing,
    InboxGuard inbox,
    ILogger<IntegrationMessageDispatcher> logger)
{
    public async Task DispatchAsync(IIntegrationMessage message, string module, CancellationToken cancellationToken = default)
    {
        var failures = new List<Exception>();

        foreach (var subscription in routing.For(module, message.GetType()))
        {
            var handlerName = subscription.HandlerType.FullName ?? subscription.HandlerType.Name;
            try
            {
                await inbox.RunOnceAsync(message.MessageId, handlerName,
                    () => subscription.Invoke(serviceProvider, message, cancellationToken), cancellationToken);
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                logger.LogError(ex, "Handler {Handler} (modul {Module}) selhal při zpracování {Type} {MessageId}",
                    subscription.HandlerType.Name, module, message.GetType().Name, message.MessageId);
                failures.Add(ex);
            }
        }

        if (failures.Count > 0)
        {
            throw new AggregateException("Některé handlery integrační zprávy selhaly.", failures);
        }
    }
}
