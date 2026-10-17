namespace MiniShop.Contracts.Inventory;

/// <summary>
/// Příkaz pro sklad (krok 9): zarezervuj zboží pro objednávku. Posílá ho orchestrátor ságy objednávky.
/// Sklad odpoví událostí <see cref="StockReservedV1"/> (vše zarezervováno), nebo <see cref="StockReservationFailedV1"/> (nic).
/// </summary>
public sealed record ReserveStockV1(
    Guid CommandId,
    DateTime OccurredAtUtc,
    int OrderId,
    IReadOnlyList<StockLineV1> Lines) : IIntegrationCommand;
