using MiniShop.Contracts.Inventory;

namespace MiniShop.Application.Ordering.Catalog;

/// <summary>
/// Obnova kopie katalogu: požádá sklad příkazem <see cref="SyncCatalogV1"/>, aby znovu poslal všechny produkty.
/// Odpověď (<see cref="ProductUpdatedV1"/>) zpracuje <see cref="CatalogProjectionHandler"/> jako každou jinou změnu.
/// </summary>
/// <remarks>
/// Přirovnání: prodejna přišla o ceník. Sklad posílá oběžníky jen při změně cen, takže si o celý ceník musí říct sama.
/// Obnova je bezpečná i opakovaně – projekce starší stav přes novější nepřepíše.
/// </remarks>
public sealed class CatalogSyncService(
    ICatalogProductRepository catalog,
    IOrderingMessagePublisher publisher,
    IOrderingUnitOfWork unitOfWork,
    TimeProvider timeProvider)
{
    /// <summary>Po startu služby: když je kopie katalogu prázdná (nová databáze), požádá sklad o celý katalog.</summary>
    /// <returns>true, když o katalog požádala.</returns>
    public async Task<bool> RequestIfEmptyAsync(CancellationToken cancellationToken = default)
    {
        if (await catalog.AnyAsync(cancellationToken))
        {
            return false;
        }

        await RequestAsync(cancellationToken);
        return true;
    }

    /// <summary>Požádá sklad o celý katalog (např. tlačítkem na stránce Objednávky).</summary>
    public async Task RequestAsync(CancellationToken cancellationToken = default)
    {
        // Přes outbox Orderingu – když sklad nebo broker zrovna neběží, žádost počká a odejde později.
        publisher.Send(new SyncCatalogV1(Guid.NewGuid(), timeProvider.GetUtcNow().UtcDateTime));
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
