using MiniShop.Application.Abstractions;
using MiniShop.Domain.Orders.Events;

namespace MiniShop.Application.Ordering.EventHandlers;

/// <summary>
/// Po doručení (osobní odběr: vyzvednutí) pošle zákazníkovi poděkování. E-shop krok 6: dřív „po vyřízení“.
/// </summary>
public sealed class SendCompletionNoticeHandler(IOrderNotifier notifier) : IDomainEventHandler<OrderCompleted>
{
    public Task HandleAsync(OrderCompleted domainEvent, CancellationToken cancellationToken) =>
        notifier.OrderCompletedAsync(domainEvent.Order, cancellationToken);
}
