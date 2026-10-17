namespace MiniShop.Contracts.Inventory;

/// <summary>
/// Příkaz pro sklad: "pošli mi celý katalog znovu". Posílá ho Ordering, když přišel o svou kopii katalogu
/// (nová databáze) nebo když o obnovu požádá uživatel. Sklad odpoví <see cref="ProductUpdatedV1"/> pro každý produkt.
/// </summary>
/// <remarks>
/// Kopie dat (read model) plněná jen událostmi "něco se změnilo" se sama znovu postavit neumí – bez tohoto příkazu
/// by po ztrátě zůstala prázdná, dokud se ve skladu nezmění každý produkt.
/// </remarks>
public sealed record SyncCatalogV1(
    Guid CommandId,
    DateTime OccurredAtUtc) : IIntegrationCommand;
