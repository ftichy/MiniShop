using MiniShop.Application.Abstractions;
using MiniShop.Domain.Orders.Events;

namespace MiniShop.Application.Orders.EventHandlers;

/// <summary>
/// Po vytvoření objednávky pošle zákazníkovi potvrzení.
/// Handler rozhoduje KDY poslat zprávu, IOrderNotifier JAK ji poslat (log, e-mail, ...).
/// </summary>
public sealed class SendOrderConfirmationHandler(IOrderNotifier notifier) : IDomainEventHandler<OrderPlaced>
{
    public Task HandleAsync(OrderPlaced domainEvent, CancellationToken cancellationToken) =>
        notifier.OrderPlacedAsync(domainEvent.Order, cancellationToken);
}
