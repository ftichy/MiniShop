namespace MiniShop.Contracts.Reviews;

/// <summary>
/// E-shop krok 14: příkaz pro Ordering – „pošli mi nákupy všech objednávek“. Modul Reviews je nový a o objednávkách
/// z doby před ním nic neví; Ordering odpoví <see cref="Orders.OrderSnapshotV2"/> pro každou objednávku.
/// </summary>
/// <remarks>Stejný vzor jako <see cref="Reporting.RebuildSalesReportV1"/> a <see cref="Inventory.SyncCatalogV1"/>.</remarks>
public sealed record SyncPurchasesV1(
    Guid CommandId,
    DateTime OccurredAtUtc) : IIntegrationCommand;
