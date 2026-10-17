namespace MiniShop.Contracts.Inventory;

/// <summary>Sklad zarezervoval všechny položky objednávky.</summary>
public sealed record StockReservedV1(
    Guid EventId,
    DateTime OccurredAtUtc,
    int OrderId) : IIntegrationEvent;
