namespace MiniShop.Application.Ordering.Catalog;

/// <summary>E-shop krok 2: řazení výlohy.</summary>
public enum CatalogSort
{
    Name,
    PriceAscending,
    PriceDescending
}

/// <summary>E-shop krok 2: co zákazník ve výloze hledá. Prázdné hodnoty = bez filtru.</summary>
public sealed record CatalogSearchRequest(
    string? Query = null,
    int? CategoryId = null,
    bool InStockOnly = false,
    CatalogSort Sort = CatalogSort.Name,
    int Page = 1);

/// <summary>Kategorie ve filtru výlohy – jen ty, ve kterých je něco v nabídce.</summary>
public sealed record CatalogCategoryDto(int Id, string Name, int ProductCount);

/// <summary>Jedna stránka výsledků + kategorie pro filtr.</summary>
public sealed record CatalogSearchResult(
    IReadOnlyList<CatalogItemDto> Items,
    int TotalCount,
    int Page,
    int PageCount,
    IReadOnlyList<CatalogCategoryDto> Categories);
