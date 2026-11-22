using MiniShop.Contracts.Inventory;
using MiniShop.Contracts.Reporting;

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
    /// <summary>
    /// Po startu služby: když je kopie katalogu prázdná (nová databáze) nebo neúplná (e-shop krok 2: produkty bez
    /// dostupnosti z doby před výlohou), požádá sklad o celý katalog.
    /// </summary>
    /// <returns>true, když o katalog požádala.</returns>
    public async Task<bool> RequestIfEmptyOrIncompleteAsync(CancellationToken cancellationToken = default)
    {
        if (await catalog.AnyAsync(cancellationToken) && !await catalog.AnyIncompleteAsync(cancellationToken))
        {
            return false;
        }

        await RequestAsync(cancellationToken);
        return true;
    }

    /// <summary>
    /// E-shop krok 27B: po startu vedoucí kopie – požádá Reporting o všechna doporučení (<see cref="SyncProductRecommendationsV1"/>),
    /// když kopie katalogu má produkty. Doplní, co kopie zmeškala (první nasazení 27B – zprávy z 27A nikdo neposlouchal).
    /// </summary>
    /// <remarks>
    /// <para>Žádá Ordering, ne Reporting po svém startu: služba se nasazuje dřív než Web, takže by zprávy poslala, když je
    /// ještě nikdo neposlouchá.</para>
    /// <para>Pokaždé, ne jen když doporučení chybí: při postupném nasazení může odpověď vyzvednout stará kopie Webu bez
    /// handleru (zprávu potvrdí a zahodí) – příští start to doplní. Stojí to desítky zpráv; stejný čas výpočtu = beze změny.
    /// Prázdná kopie katalogu (nová databáze) se doplní napřed, doporučení při dalším startu.</para>
    /// </remarks>
    /// <returns>true, když o doporučení požádala.</returns>
    public async Task<bool> RequestRecommendationsAsync(CancellationToken cancellationToken = default)
    {
        if (!await catalog.AnyAsync(cancellationToken))
        {
            return false;
        }

        publisher.Send(new SyncProductRecommendationsV1(Guid.NewGuid(), timeProvider.GetUtcNow().UtcDateTime));
        await unitOfWork.SaveChangesAsync(cancellationToken);
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
