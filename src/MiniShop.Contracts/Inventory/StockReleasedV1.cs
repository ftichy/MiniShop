namespace MiniShop.Contracts.Inventory;

/// <summary>Sklad uvolnil rezervaci objednávky (zboží je zpět k prodeji).</summary>
public sealed record StockReleasedV1(
    Guid EventId,
    DateTime OccurredAtUtc,
    int OrderId) : IIntegrationEvent;
