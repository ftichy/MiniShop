using MiniShop.Domain.Common;

namespace MiniShop.Domain.Orders.Events;

/// <summary>E-shop krok 22: vratka po částech neprošla (admin zamítl, zboží nedorazilo včas) – důvod v OrderReturn.RejectionReason.</summary>
public sealed record OrderPartialReturnRejected(Order Order, int ReturnNumber, DateTime OccurredAtUtc) : IDomainEvent
{
    public Guid EventId { get; init; } = Guid.NewGuid();
}
