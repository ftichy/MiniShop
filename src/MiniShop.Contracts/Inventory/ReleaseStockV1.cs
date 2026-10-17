namespace MiniShop.Contracts.Inventory;

/// <summary>
/// Příkaz pro sklad (krok 9): uvolni rezervaci objednávky – kompenzace po zrušení nebo po pozdní rezervaci.
/// Sklad odpoví událostí <see cref="StockReleasedV1"/>. Když rezervace neexistuje, odpoví také (nic k vrácení).
/// </summary>
public sealed record ReleaseStockV1(
    Guid CommandId,
    DateTime OccurredAtUtc,
    int OrderId) : IIntegrationCommand;
