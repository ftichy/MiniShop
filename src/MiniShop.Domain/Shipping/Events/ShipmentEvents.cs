using MiniShop.Domain.Common;

namespace MiniShop.Domain.Shipping.Events;

/// <summary>E-shop krok 26B: dopravce zásilku přijal a přidělil číslo.</summary>
public sealed record ShipmentCreated(Shipment Shipment, DateTime OccurredAtUtc) : IDomainEvent
{
    public Guid EventId { get; init; } = Guid.NewGuid();
}

/// <summary>E-shop krok 26B: dopravce zásilku odmítl (trvalá chyba).</summary>
public sealed record ShipmentFailed(Shipment Shipment, DateTime OccurredAtUtc) : IDomainEvent
{
    public Guid EventId { get; init; } = Guid.NewGuid();
}
