namespace MiniShop.Contracts.Reporting;

/// <summary>
/// E-shop krok 27B: příkaz pro Reporting – „pošli mi znovu všechna doporučení“. Ordering ho pošle po startu, když kopie
/// katalogu žádná doporučení nemá (první nasazení 27B – zprávy z 27A nikdo neposlouchal; nová databáze). Reporting odpoví
/// <see cref="ProductRecommendationsChangedV1"/> pro každý produkt s původním časem výpočtu.
/// </summary>
/// <remarks>Stejný vzor jako <see cref="RebuildSalesReportV1"/> a <see cref="Inventory.SyncCatalogV1"/>.</remarks>
public sealed record SyncProductRecommendationsV1(
    Guid CommandId,
    DateTime OccurredAtUtc) : IIntegrationCommand;
