using MiniShop.Domain.Common;

namespace MiniShop.Domain.Orders.Events;

/// <summary>E-shop krok 12: vrácení neprošlo – admin zamítl nebo zboží nedorazilo včas (důvod v Order.ReturnRejectionReason).</summary>
public sealed record OrderReturnRejected(Order Order, DateTime OccurredAtUtc) : IDomainEvent
{
    public Guid EventId { get; init; } = Guid.NewGuid();
}
