using MiniShop.Domain.Common;

namespace MiniShop.Domain.Orders.Events;

/// <summary>E-shop krok 12: vrácení dokončené – zboží zpět na skladu, peníze vrácené (kartou) nebo je vrací admin ručně.</summary>
public sealed record OrderReturned(Order Order, DateTime OccurredAtUtc) : IDomainEvent
{
    public Guid EventId { get; init; } = Guid.NewGuid();
}
