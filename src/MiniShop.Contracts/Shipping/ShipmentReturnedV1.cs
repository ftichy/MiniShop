namespace MiniShop.Contracts.Shipping;

/// <summary>
/// E-shop krok 26D: zásilku se nepodařilo doručit a dopravce ji vrátil odesílateli. Ordering z ní udělá fakt
/// o objednávce (OrderReturnedToSenderV1), na který si každý modul zareaguje sám (choreografie).
/// <paramref name="ReturnedAtUtc"/> = kdy se to u dopravce stalo (webhook může přijít později).
/// </summary>
public sealed record ShipmentReturnedV1(
    Guid EventId,
    DateTime OccurredAtUtc,
    int OrderId,
    string TrackingNumber,
    DateTime ReturnedAtUtc) : IIntegrationEvent;
