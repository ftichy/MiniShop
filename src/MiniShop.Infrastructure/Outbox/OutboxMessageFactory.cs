using System.Text.Json;
using MiniShop.Contracts;
using MiniShop.Domain.Common;
using MiniShop.Domain.Orders;
using MiniShop.Domain.Orders.Events;

namespace MiniShop.Infrastructure.Outbox;

/// <summary>
/// Převede doménovou událost na řádek outboxu. Payload obsahuje jen data (ID, e-mail, položky),
/// protože událost uložená jako JSON nemůže nést odkaz na entitu. (Dobrý nácvik na integrační události v kroku 7.)
/// </summary>
/// <remarks>
/// Volá se až po prvním uložení v transakci – objednávka už má přidělené ID.
/// Neznámá událost vyhodí výjimku: transakce se vrátí a chyba se hned ukáže, místo aby se událost tiše ztratila.
/// </remarks>
public static class OutboxMessageFactory
{
    public static OutboxMessage Create(IDomainEvent domainEvent) => new()
    {
        Id = domainEvent.EventId,   // ID řádku = ID události, aby ho worker mohl vrátit do znovu sestavené události
        Kind = OutboxMessageKind.Domain,
        Type = domainEvent.GetType().Name,
        Payload = JsonSerializer.Serialize(ToPayload(domainEvent)),
        OccurredAtUtc = domainEvent.OccurredAtUtc
    };

    /// <summary>
    /// Integrační událost (krok 7) je už sama o sobě jen data – payload je přímo její JSON, žádný převod.
    /// Type je název kontraktu včetně verze ("OrderPlacedV1"), podle něj ji <see cref="IntegrationEventReader"/> načte zpět.
    /// </summary>
    public static OutboxMessage Create(IIntegrationEvent integrationEvent) => new()
    {
        Id = integrationEvent.EventId,
        Kind = OutboxMessageKind.Integration,
        Type = integrationEvent.GetType().Name,
        Payload = JsonSerializer.Serialize(integrationEvent, integrationEvent.GetType()),
        OccurredAtUtc = integrationEvent.OccurredAtUtc
    };

    private static object ToPayload(IDomainEvent domainEvent) => domainEvent switch
    {
        OrderPlaced e => new OrderPayload(e.Order.Id, e.Order.CustomerEmail, e.Order.TotalPrice, ToLines(e.Order)),
        OrderCompleted e => new OrderPayload(e.Order.Id, e.Order.CustomerEmail, e.Order.TotalPrice, ToLines(e.Order)),
        OrderCancelled e => new OrderPayload(e.Order.Id, e.Order.CustomerEmail, e.Order.TotalPrice, ToLines(e.Order)),
        _ => throw new InvalidOperationException(
            $"Událost {domainEvent.GetType().Name} nemá převod do outboxu. Doplňte ji do {nameof(OutboxMessageFactory)}.")
    };

    private static IReadOnlyList<OrderLinePayload> ToLines(Order order) =>
        order.Items.Select(i => new OrderLinePayload(i.ProductId, i.Quantity)).ToList();
}

/// <summary>Data objednávky v outboxu (společná pro OrderPlaced/Completed/Cancelled).</summary>
public sealed record OrderPayload(int OrderId, string CustomerEmail, decimal TotalPrice, IReadOnlyList<OrderLinePayload> Lines);

public sealed record OrderLinePayload(int ProductId, int Quantity);
