namespace MiniShop.Contracts.Inventory;

/// <summary>Sklad nemohl rezervaci provést (nedostatek zboží, neznámý produkt). Nic nezarezervoval.</summary>
public sealed record StockReservationFailedV1(
    Guid EventId,
    DateTime OccurredAtUtc,
    int OrderId,
    string Reason) : IIntegrationEvent;
