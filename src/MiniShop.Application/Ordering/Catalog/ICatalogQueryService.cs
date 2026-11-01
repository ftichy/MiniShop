namespace MiniShop.Application.Ordering.Catalog;

/// <summary>
/// E-shop krok 2: čtení výlohy – hledání, filtry, řazení, stránkování nad kopií katalogu v Orderingu.
/// Samostatné rozhraní (ne IOrderService): výloha jen čte a objednávky nepotřebuje (ISP, CQRS – strana čtení).
/// </summary>
public interface ICatalogQueryService
{
    Task<CatalogSearchResult> SearchAsync(CatalogSearchRequest request, CancellationToken cancellationToken = default);
}
