namespace MiniShop.Contracts.Shipping;

/// <summary>
/// E-shop krok 26C: dopravce zásilku doručil – Ordering objednávku označí jako doručenou (OrderCompletedV1 dál jako dřív).
/// <paramref name="DeliveredAtUtc"/> = kdy se to u dopravce stalo (webhook může přijít později).
/// </summary>
public sealed record ShipmentDeliveredV1(
    Guid EventId,
    DateTime OccurredAtUtc,
    int OrderId,
    string TrackingNumber,
    DateTime DeliveredAtUtc) : IIntegrationEvent;
