namespace MiniShop.Contracts.Inventory;

/// <summary>
/// Odpověď skladu na <see cref="RestockReturnedGoodsV2"/> (e-shop krok 22): zboží vratky je zpět na skladu.
/// </summary>
/// <param name="Quantity">Kolik kusů se opravdu naskladnilo (smazaný produkt se nenaskladní).</param>
public sealed record ReturnedGoodsRestockedV2(
    Guid EventId,
    DateTime OccurredAtUtc,
    int OrderId,
    int ReturnNumber,
    int Quantity) : IIntegrationEvent;
