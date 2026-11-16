using MiniShop.Domain.Common;

namespace MiniShop.Domain.Orders.Events;

/// <summary>E-shop krok 22: zákazník chce vrátit vybrané kusy (vratka po částech) – startuje ságu vratky s číslem vratky.</summary>
public sealed record OrderPartialReturnRequested(Order Order, int ReturnNumber, DateTime OccurredAtUtc) : IDomainEvent
{
    public Guid EventId { get; init; } = Guid.NewGuid();
}
