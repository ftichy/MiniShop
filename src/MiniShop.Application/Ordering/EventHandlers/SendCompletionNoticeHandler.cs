using MiniShop.Application.Abstractions;
using MiniShop.Domain.Orders.Events;

namespace MiniShop.Application.Ordering.EventHandlers;

/// <summary>
/// Po vyřízení objednávky pošle zákazníkovi oznámení.
/// </summary>
public sealed class SendCompletionNoticeHandler(IOrderNotifier notifier) : IDomainEventHandler<OrderCompleted>
{
    public Task HandleAsync(OrderCompleted domainEvent, CancellationToken cancellationToken) =>
        notifier.OrderCompletedAsync(domainEvent.Order, cancellationToken);
}
