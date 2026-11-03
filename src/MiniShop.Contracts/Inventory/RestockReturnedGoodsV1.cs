namespace MiniShop.Contracts.Inventory;

/// <summary>
/// Příkaz pro sklad (e-shop krok 12): vrácené zboží objednávky dorazilo v pořádku – naskladni ho podle vydané rezervace.
/// Sklad odpoví <see cref="ReturnedGoodsRestockedV1"/>, i když už bylo naskladněné (opakování) nebo rezervace chybí.
/// </summary>
public sealed record RestockReturnedGoodsV1(
    Guid CommandId,
    DateTime OccurredAtUtc,
    int OrderId) : IIntegrationCommand;
