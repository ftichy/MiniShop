using MiniShop.Application.Abstractions;
using MiniShop.Domain.Orders.Events;

namespace MiniShop.Application.Ordering.EventHandlers;

/// <summary>
/// E-shop krok 6: po odeslání pošle zákazníkovi dopravce a číslo zásilky (osobní odběr: „připraveno k vyzvednutí“).
/// </summary>
public sealed class SendShippingNoticeHandler(IOrderNotifier notifier) : IDomainEventHandler<OrderShipped>
{
    public Task HandleAsync(OrderShipped domainEvent, CancellationToken cancellationToken) =>
        notifier.OrderShippedAsync(domainEvent.Order, cancellationToken);
}
