using MiniShop.Application.Abstractions;
using MiniShop.Contracts.Reporting;
using MiniShop.Domain.Reporting;

namespace MiniShop.Application.Reporting;

/// <summary>E-shop krok 27A: naposledy odeslaná doporučení „Zákazníci také koupili“ (jeden řádek na produkt).</summary>
public interface IProductRecommendationRepository
{
    Task<IReadOnlyList<ProductRecommendation>> GetAllAsync(CancellationToken cancellationToken = default);

    void Add(ProductRecommendation recommendation);
}

/// <summary>
/// E-shop krok 27A: dávka „Zákazníci také koupili“ – odvozený čtecí model z jiného čtecího modelu. Z objednávek
/// v přehledu tržeb (<see cref="SalesOrder"/>, jen ty, které si zákazník nechal) spočítá dvojice koupené spolu
/// (<see cref="BoughtTogether"/>) a pro produkty, kterým se seznam změnil, pošle <see cref="ProductRecommendationsChangedV1"/>.
/// </summary>
/// <remarks>
/// Dávka místo reakce na každou zprávu: doporučení nemusí být aktuální na vteřinu a přepočet z celých dat je jednodušší
/// než upravovat čítače po každé události (a sám opraví cokoli, co se pokazilo). Uložení „co jsme poslali“ a zprávy
/// do outboxu je jedna transakce – po pádu uprostřed se nic neztratí ani nepošle dvakrát jinak.
/// </remarks>
public sealed class ProductRecommendationService(
    ISalesOrderRepository orders,
    IProductRecommendationRepository recommendations,
    IReportingMessagePublisher publisher,
    IReportingUnitOfWork unitOfWork,
    TimeProvider timeProvider)
{
    /// <summary>Z jakého období objednávky počítat – starší nákupy už o dnešním sortimentu moc neříkají.</summary>
    public const int WindowDays = 180;

    /// <returns>Kolika produktům se doporučení změnila (tolik zpráv odešlo).</returns>
    public async Task<int> RecomputeAsync(CancellationToken cancellationToken = default)
    {
        // Přestavění přehledu tržeb smaže všechny objednávky – z prázdných dat by dávka „smazala“ všechna doporučení.
        if (!await orders.AnyAsync(cancellationToken))
        {
            return 0;
        }

        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        var placed = await orders.GetPlacedSinceAsync(nowUtc.AddDays(-WindowDays), cancellationToken);
        var computed = BoughtTogether.Compute(placed
            .Where(o => o.CountsAsRevenue)                       // bez zamítnutých, zrušených, vrácených
            .Select(o => o.KeptLines().Select(l => l.ProductId))); // bez kusů vrácených po částech

        var existing = (await recommendations.GetAllAsync(cancellationToken)).ToDictionary(r => r.ProductId);
        var changed = 0;
        foreach (var (productId, recommended) in computed)
        {
            if (existing.Remove(productId, out var known))
            {
                if (!known.Update(recommended, nowUtc))
                {
                    continue;
                }
            }
            else
            {
                recommendations.Add(ProductRecommendation.Create(productId, recommended, nowUtc));
            }

            Publish(productId, recommended, nowUtc);
            changed++;
        }

        // Produkty, které dřív něco doporučovaly a teď ne (zrušené, vrácené objednávky) – poslat prázdný seznam.
        foreach (var gone in existing.Values.Where(r => r.Update([], nowUtc)))
        {
            Publish(gone.ProductId, [], nowUtc);
            changed++;
        }

        if (changed > 0)
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        TraceTags.Tag("minishop.recommendations.changed", changed);
        return changed;
    }

    private void Publish(int productId, IReadOnlyList<int> recommended, DateTime nowUtc) =>
        publisher.Publish(new ProductRecommendationsChangedV1(Guid.NewGuid(), nowUtc, productId, recommended, nowUtc));
}
