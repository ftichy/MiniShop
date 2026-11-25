namespace MiniShop.Contracts.Shipping;

/// <summary>
/// E-shop krok 28C: dopravce převzal balík objednávky a přidělil mu číslo zásilky. Jako <see cref="ShipmentCreatedV1"/>
/// s číslem balíku (Shipping posílá zatím obě verze – expand → contract).
/// </summary>
public sealed record ShipmentCreatedV2(
    Guid EventId,
    DateTime OccurredAtUtc,
    int OrderId,
    int ParcelNumber,
    string Carrier,
    string TrackingNumber) : IIntegrationEvent;

/// <summary>E-shop krok 28C: dopravce balík odmítl (trvalá chyba) – jako <see cref="ShipmentFailedV1"/> s číslem balíku.</summary>
public sealed record ShipmentFailedV2(
    Guid EventId,
    DateTime OccurredAtUtc,
    int OrderId,
    int ParcelNumber,
    string Reason) : IIntegrationEvent;
