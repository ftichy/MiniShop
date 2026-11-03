namespace MiniShop.Contracts.Reporting;

/// <summary>
/// E-shop krok 11: příkaz pro Ordering – „pošli mi znovu všechny objednávky“. Reporting si smazal přehled
/// tržeb (nebo ho ještě nemá) a postaví ho znovu ze snímků <see cref="Orders.OrderSnapshotV1"/>.
/// </summary>
/// <remarks>Stejný vzor jako obnova kopie katalogu (<see cref="Inventory.SyncCatalogV1"/>).</remarks>
public sealed record RebuildSalesReportV1(
    Guid CommandId,
    DateTime OccurredAtUtc) : IIntegrationCommand;
