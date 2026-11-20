namespace MiniShop.Contracts.Shipping;

/// <summary>E-shop krok 26B: dopravce zásilku převzal do systému a přidělil jí číslo – Ordering objednávku odešle.</summary>
public sealed record ShipmentCreatedV1(
    Guid EventId,
    DateTime OccurredAtUtc,
    int OrderId,
    string Carrier,
    string TrackingNumber) : IIntegrationEvent;
