using MiniShop.Domain.Common;

namespace MiniShop.Domain.Orders.Events;

/// <summary>E-shop krok 8: zboží je zarezervované, objednávka čeká na platbu kartou.</summary>
public sealed record OrderAwaitingPayment(Order Order, DateTime OccurredAtUtc) : IDomainEvent
{
    public Guid EventId { get; init; } = Guid.NewGuid();
}
