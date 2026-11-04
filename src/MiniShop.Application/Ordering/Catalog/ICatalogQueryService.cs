namespace MiniShop.Application.Ordering.Catalog;

/// <summary>
/// E-shop krok 2: čtení výlohy – hledání, filtry, řazení, stránkování nad kopií katalogu v Orderingu.
/// Samostatné rozhraní (ne IOrderService): výloha jen čte a objednávky nepotřebuje (ISP, CQRS – strana čtení).
/// </summary>
public interface ICatalogQueryService
{
    Task<CatalogSearchResult> SearchAsync(CatalogSearchRequest request, CancellationToken cancellationToken = default);

    /// <summary>E-shop krok 14: jeden produkt pro stránku detailu (/Shop/{id}); null = neexistuje nebo je stažený z nabídky.</summary>
    Task<CatalogItemDto?> GetProductAsync(int productId, CancellationToken cancellationToken = default);
}
