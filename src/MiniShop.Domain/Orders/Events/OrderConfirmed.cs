using MiniShop.Domain.Common;

namespace MiniShop.Domain.Orders.Events;

/// <summary>Sklad zarezervoval zboží a objednávka je potvrzená (krok 9).</summary>
public sealed record OrderConfirmed(Order Order, DateTime OccurredAtUtc) : IDomainEvent
{
    public Guid EventId { get; init; } = Guid.NewGuid();
}
