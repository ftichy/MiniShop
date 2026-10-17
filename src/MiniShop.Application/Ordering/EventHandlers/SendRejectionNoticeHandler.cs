using MiniShop.Application.Abstractions;
using MiniShop.Domain.Orders.Events;

namespace MiniShop.Application.Orders.EventHandlers;

/// <summary>
/// Po zamítnutí objednávky (zboží nebylo nebo sklad neodpověděl, krok 9) pošle zákazníkovi omluvu s důvodem.
/// </summary>
public sealed class SendRejectionNoticeHandler(IOrderNotifier notifier) : IDomainEventHandler<OrderRejected>
{
    public Task HandleAsync(OrderRejected domainEvent, CancellationToken cancellationToken) =>
        notifier.OrderRejectedAsync(domainEvent.Order, cancellationToken);
}
