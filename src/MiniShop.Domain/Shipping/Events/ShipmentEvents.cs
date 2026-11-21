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

/// <summary>E-shop krok 26C: dopravce zásilku doručil (webhook nebo dohledání).</summary>
public sealed record ShipmentDelivered(Shipment Shipment, DateTime OccurredAtUtc) : IDomainEvent
{
    public Guid EventId { get; init; } = Guid.NewGuid();
}

/// <summary>
/// Vzhled krok F3b: stav zásilky u dopravce se změnil (převzato, na cestě, nezastižen…). <paramref name="Status"/> = stav
/// v okamžiku změny (dohledání může v jedné transakci dohnat víc kroků).
/// </summary>
public sealed record ShipmentStatusChanged(Shipment Shipment, ShipmentStatus Status, DateTime OccurredAtUtc) : IDomainEvent
{
    public Guid EventId { get; init; } = Guid.NewGuid();
}

/// <summary>E-shop krok 26D: zásilku se nepodařilo doručit a dopravce ji vrátil odesílateli.</summary>
public sealed record ShipmentReturned(Shipment Shipment, DateTime OccurredAtUtc) : IDomainEvent
{
    public Guid EventId { get; init; } = Guid.NewGuid();
}
