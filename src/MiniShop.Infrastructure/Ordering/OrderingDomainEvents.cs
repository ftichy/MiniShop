using MiniShop.Domain.Common;
using MiniShop.Domain.Orders;
using MiniShop.Domain.Orders.Events;
using MiniShop.Infrastructure.Outbox;

namespace MiniShop.Infrastructure.Ordering;

/// <summary>
/// Převede doménovou událost objednávky na řádek outboxu (krok 4). Payload obsahuje jen data (ID, e-mail, položky),
/// protože událost uložená jako JSON nemůže nést odkaz na entitu.
/// </summary>
/// <remarks>
/// Volá se až po prvním uložení v transakci – objednávka už má přidělené ID.
/// Neznámá událost vyhodí výjimku: transakce se vrátí a chyba se hned ukáže, místo aby se událost tiše ztratila.
/// </remarks>
public static class OrderingDomainEvents
{
    public static OutboxMessage ToOutboxMessage(IDomainEvent domainEvent) => domainEvent switch
    {
        OrderPlaced e => OutboxMessageFactory.Create(e, ToPayload(e.Order)),
        OrderConfirmed e => OutboxMessageFactory.Create(e, ToPayload(e.Order)),
        OrderRejected e => OutboxMessageFactory.Create(e, ToPayload(e.Order)),
        OrderCompleted e => OutboxMessageFactory.Create(e, ToPayload(e.Order)),
        OrderCancelled e => OutboxMessageFactory.Create(e, ToPayload(e.Order)),
        _ => throw new InvalidOperationException(
            $"Událost {domainEvent.GetType().Name} nemá převod do outboxu. Doplňte ji do {nameof(OrderingDomainEvents)}.")
    };

    private static OrderPayload ToPayload(Order order) => new(
        order.Id,
        order.CustomerEmail,
        order.TotalPrice,
        order.Items.Select(i => new OrderLinePayload(i.ProductId, i.Quantity)).ToList());
}

/// <summary>Data objednávky v outboxu (společná pro všechny události objednávky).</summary>
public sealed record OrderPayload(int OrderId, string CustomerEmail, decimal TotalPrice, IReadOnlyList<OrderLinePayload> Lines);

public sealed record OrderLinePayload(int ProductId, int Quantity);
