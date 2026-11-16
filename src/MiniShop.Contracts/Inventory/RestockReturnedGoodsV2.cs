namespace MiniShop.Contracts.Inventory;

/// <summary>
/// Příkaz pro sklad (e-shop krok 22): vrácené zboží jedné vratky dorazilo v pořádku – naskladni jen uvedené kusy.
/// Na rozdíl od <see cref="RestockReturnedGoodsV1"/> (celá objednávka) nese číslo vratky a řádky – k jedné objednávce
/// může být víc vratek. Sklad odpoví <see cref="ReturnedGoodsRestockedV2"/>, i když už vratku naskladnil (opakování).
/// </summary>
/// <param name="ReturnNumber">Pořadí vratky v objednávce (1, 2, …) – spolu s OrderId jednoznačně určuje vratku.</param>
/// <param name="Lines">Které produkty a kolik kusů se vrací.</param>
public sealed record RestockReturnedGoodsV2(
    Guid CommandId,
    DateTime OccurredAtUtc,
    int OrderId,
    int ReturnNumber,
    IReadOnlyList<StockLineV1> Lines) : IIntegrationCommand;
