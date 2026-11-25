namespace MiniShop.Contracts.Inventory;

/// <summary>
/// E-shop krok 28B (směr C): zboží je zarezervované – a odkud. Jeden balík za každý sklad, ze kterého se zboží vzalo.
/// Expand → contract: sklad posílá V2 vedle <see cref="StockReservedV1"/> – na V1 dál čeká sága (potvrzení objednávky),
/// V2 si Ordering jen uloží jako balíky objednávky.
/// </summary>
/// <param name="Parcels">Balíky (sklad + položky), aspoň jeden.</param>
public sealed record StockReservedV2(
    Guid EventId,
    DateTime OccurredAtUtc,
    int OrderId,
    IReadOnlyList<StockParcelV1> Parcels) : IIntegrationEvent;

/// <summary>Balík ve zprávě <see cref="StockReservedV2"/> – kód a název skladu (Ordering sklady nezná) a položky.</summary>
public sealed record StockParcelV1(string WarehouseCode, string WarehouseName, IReadOnlyList<StockLineV1> Lines);
