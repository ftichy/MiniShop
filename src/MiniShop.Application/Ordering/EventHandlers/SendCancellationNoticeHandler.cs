using MiniShop.Application.Abstractions;
using MiniShop.Domain.Orders.Events;

namespace MiniShop.Application.Ordering.EventHandlers;

/// <summary>
/// Po zrušení objednávky pošle zákazníkovi oznámení.
/// </summary>
public sealed class SendCancellationNoticeHandler(IOrderNotifier notifier) : IDomainEventHandler<OrderCancelled>
{
    public Task HandleAsync(OrderCancelled domainEvent, CancellationToken cancellationToken) =>
        notifier.OrderCancelledAsync(domainEvent.Order, cancellationToken);
}
