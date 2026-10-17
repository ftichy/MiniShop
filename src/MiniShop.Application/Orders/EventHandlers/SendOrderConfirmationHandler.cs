using MiniShop.Application.Abstractions;
using MiniShop.Domain.Orders.Events;

namespace MiniShop.Application.Orders.EventHandlers;

/// <summary>
/// Po potvrzení objednávky (sklad zarezervoval, krok 9) pošle zákazníkovi potvrzení.
/// Handler rozhoduje KDY poslat zprávu, IOrderNotifier JAK ji poslat (log, e-mail, ...).
/// </summary>
public sealed class SendOrderConfirmationHandler(IOrderNotifier notifier) : IDomainEventHandler<OrderConfirmed>
{
    public Task HandleAsync(OrderConfirmed domainEvent, CancellationToken cancellationToken) =>
        notifier.OrderConfirmedAsync(domainEvent.Order, cancellationToken);
}
