using System.Globalization;
using MiniShop.Domain.Orders;

namespace MiniShop.Application.Ordering.Catalog;

/// <summary>
/// E-shop krok 2: výloha nad kopií katalogu. Funguje i při výpadku skladu – na sklad se vůbec neptá.
/// </summary>
/// <remarks>
/// Filtr (text, kategorie, „jen skladem“) dělá databáze; řazení a stránkování až tady v paměti – SQLite neumí řadit
/// podle decimal (cena) a české řazení názvů. Katalog má jednotky až stovky produktů, takže to nevadí.
/// Při tisících produktů by cena šla ukládat i jako celé haléře (long) a řadit v SQL.
/// </remarks>
public sealed class CatalogQueryService(ICatalogProductRepository catalog) : ICatalogQueryService
{
    public const int PageSize = 12;

    private static readonly StringComparer CzechOrder = StringComparer.Create(new CultureInfo("cs-CZ"), ignoreCase: true);

    public async Task<CatalogSearchResult> SearchAsync(CatalogSearchRequest request, CancellationToken cancellationToken = default)
    {
        var found = await catalog.SearchAsync(
            CatalogSearchText.Normalize(request.Query), request.CategoryId, request.InStockOnly, cancellationToken);

        IEnumerable<CatalogProduct> sorted = request.Sort switch
        {
            CatalogSort.PriceAscending => found.OrderBy(p => p.Price).ThenBy(p => p.Name, CzechOrder),
            CatalogSort.PriceDescending => found.OrderByDescending(p => p.Price).ThenBy(p => p.Name, CzechOrder),
            _ => found.OrderBy(p => p.Name, CzechOrder)
        };

        var pageCount = Math.Max(1, (int)Math.Ceiling(found.Count / (double)PageSize));
        var page = Math.Clamp(request.Page, 1, pageCount);
        var items = sorted.Skip((page - 1) * PageSize).Take(PageSize).Select(p => p.ToCatalogItemDto()).ToList();

        return new CatalogSearchResult(items, found.Count, page, pageCount, await GetCategoriesAsync(cancellationToken));
    }

    /// <summary>Kategorie s počtem produktů v nabídce – nezávisle na hledání, ať jde filtr kdykoli změnit.</summary>
    private async Task<IReadOnlyList<CatalogCategoryDto>> GetCategoriesAsync(CancellationToken cancellationToken)
    {
        var products = await catalog.GetAvailableAsync(cancellationToken);
        return products
            .Where(p => p.CategoryId is not null)
            .GroupBy(p => p.CategoryId!.Value)
            .Select(g => new CatalogCategoryDto(g.Key, g.First().CategoryName!, g.Count()))
            .OrderBy(c => c.Name, CzechOrder)
            .ToList();
    }
}
