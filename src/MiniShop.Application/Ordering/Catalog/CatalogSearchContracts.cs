namespace MiniShop.Application.Ordering.Catalog;

/// <summary>E-shop krok 2: řazení výlohy.</summary>
public enum CatalogSort
{
    Name,
    PriceAscending,
    PriceDescending,

    /// <summary>E-shop krok 14: nejlépe hodnocené (průměr, pak počet hodnocení) – z kopie katalogu.</summary>
    BestRated,

    /// <summary>E-shop krok 21: nejlepší shoda s hledaným textem (název před kategorií a popisem). Výchozí, když se hledá.</summary>
    Relevance
}

/// <summary>E-shop krok 2: co zákazník ve výloze hledá. Prázdné hodnoty = bez filtru.</summary>
/// <param name="Sort">Null = výchozí: s hledaným textem <see cref="CatalogSort.Relevance"/>, bez něj <see cref="CatalogSort.Name"/>.</param>
public sealed record CatalogSearchRequest(
    string? Query = null,
    int? CategoryId = null,
    bool InStockOnly = false,
    CatalogSort? Sort = null,
    int Page = 1);

/// <summary>Kategorie ve filtru výlohy – jen ty, ve kterých je něco v nabídce.</summary>
public sealed record CatalogCategoryDto(int Id, string Name, int ProductCount);

/// <summary>Jedna stránka výsledků + kategorie pro filtr.</summary>
/// <param name="Sort">Řazení, které se opravdu použilo (výchozí podle toho, zda se hledá).</param>
/// <param name="IsSimilarMatch">
/// E-shop krok 21: přesně nic neodpovídalo, výsledky jsou podobná slova (překlep) – stránka to zákazníkovi řekne.
/// </param>
public sealed record CatalogSearchResult(
    IReadOnlyList<CatalogItemDto> Items,
    int TotalCount,
    int Page,
    int PageCount,
    IReadOnlyList<CatalogCategoryDto> Categories,
    CatalogSort Sort = CatalogSort.Name,
    bool IsSimilarMatch = false);

/// <summary>E-shop krok 21: produkt v našeptávači – jen to, co se vejde do řádku nabídky.</summary>
public sealed record CatalogSuggestionDto(int Id, string Name, decimal Price, string? ImageFileName, string Availability);

/// <summary>E-shop krok 21: odpověď našeptávače – kategorie (odkaz na filtr) a nejlepší produkty.</summary>
public sealed record CatalogSuggestions(
    IReadOnlyList<CatalogCategoryDto> Categories,
    IReadOnlyList<CatalogSuggestionDto> Products,
    bool IsSimilarMatch)
{
    public static CatalogSuggestions Empty { get; } = new([], [], false);
}
