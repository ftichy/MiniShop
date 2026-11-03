namespace MiniShop.Contracts.Inventory;

/// <summary>E-shop krok 12: sklad naskladnil vrácené zboží objednávky (odpověď na <see cref="RestockReturnedGoodsV1"/>).</summary>
/// <param name="Quantity">Kolik kusů se vrátilo na sklad (0 = nebylo co naskladnit – např. už naskladněno).</param>
public sealed record ReturnedGoodsRestockedV1(
    Guid EventId,
    DateTime OccurredAtUtc,
    int OrderId,
    int Quantity) : IIntegrationEvent;
