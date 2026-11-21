using MiniShop.Domain.Common;

namespace MiniShop.Domain.Orders.Events;

/// <summary>
/// Vzhled krok F3a: stav objednávky se změnil (jakkoli – vyvolá ho jediné místo, <c>Order.ChangeStatus</c>). Z ní vznikne
/// obecná událost <c>OrderStatusChangedV1</c>, podle které Web pošle otevřeným stránkám signál „obnov se“ (SignalR).
/// </summary>
/// <remarks>Konkrétní události (OrderConfirmed, OrderShipped…) zůstávají – tahle jen doplňuje přechody, které vlastní nemají.</remarks>
public sealed record OrderStatusChanged(Order Order, DateTime OccurredAtUtc) : IDomainEvent
{
    public Guid EventId { get; init; } = Guid.NewGuid();
}
