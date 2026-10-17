using MiniShop.Domain.Common;

namespace MiniShop.Domain.Orders.Events;

/// <summary>Objednávka byla zamítnuta – zboží nebylo nebo sklad neodpověděl včas (krok 9). Důvod je v Order.RejectionReason.</summary>
public sealed record OrderRejected(Order Order, DateTime OccurredAtUtc) : IDomainEvent
{
    public Guid EventId { get; init; } = Guid.NewGuid();
}
