namespace MiniShop.Contracts.Shipping;

/// <summary>
/// Vzhled krok F3b: stav zásilky u dopravce se změnil (převzato, na cestě, doručuje se, nezastižen…). Stav objednávky se
/// přitom nemění (je pořád „odeslaná“) – Web podle této události pošle otevřeným stránkám signál „obnov se“ (SignalR).
/// Doručení a vrácení dál řídí <see cref="ShipmentDeliveredV1"/> a <see cref="ShipmentReturnedV1"/>.
/// </summary>
/// <param name="Status">Nový stav zásilky v pojmech MiniShopu (<c>ShipmentStatus</c> jako text, např. InTransit).</param>
/// <param name="ChangedAtUtc">Kdy se to u dopravce stalo (webhook může přijít později).</param>
public sealed record ShipmentStatusChangedV1(
    Guid EventId,
    DateTime OccurredAtUtc,
    int OrderId,
    string TrackingNumber,
    string Status,
    DateTime ChangedAtUtc) : IIntegrationEvent;
