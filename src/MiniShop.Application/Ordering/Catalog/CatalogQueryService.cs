using System.Globalization;
using MiniShop.Application.Abstractions;
using MiniShop.Domain.Orders;

namespace MiniShop.Application.Ordering.Catalog;

/// <summary>
/// E-shop krok 2: výloha nad kopií katalogu. Funguje i při výpadku skladu – na sklad se vůbec neptá.
/// </summary>
/// <remarks>
/// Filtr (slova, kategorie, „jen skladem“) dělá databáze; řazení a stránkování až tady v paměti – české řazení názvů
/// databáze bez české kolace neumí (původně i cena – SQLite neřadilo decimal). Katalog má jednotky až stovky produktů, takže to nevadí.
/// Při tisících produktů by cena šla ukládat i jako celé haléře (long) a řadit v SQL.
/// <para>
/// E-shop krok 21: hledá se po slovech (všechna, libovolné pořadí, stačí kus slova). Když přesně nic neodpovídá,
/// zkusí se podobná slova (překlep – PostgreSQL pg_trgm) a stránka řekne „zobrazujeme podobné výsledky“.
/// Text dotazu je volný text zákazníka – do trasy ani logu nejde, jen počet výsledků.
/// </para>
/// </remarks>
public sealed class CatalogQueryService(ICatalogProductRepository catalog) : ICatalogQueryService
{
    public const int PageSize = 12;

    /// <summary>Kolik produktů ukáže našeptávač.</summary>
    public const int SuggestionLimit = 6;

    /// <summary>Kolik kategorií ukáže našeptávač.</summary>
    public const int SuggestedCategoryLimit = 3;

    /// <summary>Našeptávač začne až od tolika znaků – jedno písmeno najde skoro všechno.</summary>
    public const int SuggestionMinLength = 2;

    /// <summary>
    /// Jak podobné musí slovo být, aby se počítalo jako překlep (0–1, pg_trgm <c>word_similarity</c>).
    /// Naměřeno: „klavesnyce“ ↔ „klavesnice“ 0,64, „monitr“ ↔ „monitor“ 0,71, „notbuk“ ↔ „notebook“ 0,43; nesouvisející
    /// „mys“ ↔ „sluchatka s mikrofonem“ 0,25 – proto hranice 0,4.
    /// </summary>
    public const double SimilarityThreshold = 0.4;

    /// <summary>Nejvýš tolik výsledků hledání podobných slov – víc už jsou jen náhodné shody.</summary>
    public const int SimilarResultLimit = 48;

    private static readonly StringComparer CzechOrder = StringComparer.Create(new CultureInfo("cs-CZ"), ignoreCase: true);

    public async Task<CatalogSearchResult> SearchAsync(CatalogSearchRequest request, CancellationToken cancellationToken = default)
    {
        var words = CatalogSearchText.Words(request.Query);
        var sort = request.Sort ?? (words.Count > 0 ? CatalogSort.Relevance : CatalogSort.Name);
        var (found, scores, isSimilar) = await FindAsync(words, request.CategoryId, request.InStockOnly, cancellationToken);

        IEnumerable<CatalogProduct> sorted = sort switch
        {
            CatalogSort.PriceAscending => found.OrderBy(p => p.Price).ThenBy(p => p.Name, CzechOrder),
            CatalogSort.PriceDescending => found.OrderByDescending(p => p.Price).ThenBy(p => p.Name, CzechOrder),
            CatalogSort.BestRated => found.OrderByDescending(p => p.RatingAverage).ThenByDescending(p => p.RatingCount)
                .ThenBy(p => p.Name, CzechOrder),
            CatalogSort.Relevance => found.OrderByDescending(p => scores[p.Id]).ThenBy(p => p.Name, CzechOrder),
            _ => found.OrderBy(p => p.Name, CzechOrder)
        };

        if (words.Count > 0)
        {
            TraceTags.Tag("minishop.search.results", found.Count);
            TraceTags.Tag("minishop.search.similar", isSimilar);
        }

        var pageCount = Math.Max(1, (int)Math.Ceiling(found.Count / (double)PageSize));
        var page = Math.Clamp(request.Page, 1, pageCount);
        var items = sorted.Skip((page - 1) * PageSize).Take(PageSize).Select(p => p.ToCatalogItemDto()).ToList();

        return new CatalogSearchResult(items, found.Count, page, pageCount, await GetCategoriesAsync(cancellationToken), sort, isSimilar);
    }

    public async Task<CatalogSuggestions> SuggestAsync(string? query, CancellationToken cancellationToken = default)
    {
        var words = CatalogSearchText.Words(query);
        if (string.Join(' ', words).Length < SuggestionMinLength)
        {
            return CatalogSuggestions.Empty;
        }

        var (found, scores, isSimilar) = await FindAsync(words, categoryId: null, inStockOnly: false, cancellationToken);
        var products = found
            .OrderByDescending(p => scores[p.Id])
            .ThenBy(p => p.Name, CzechOrder)
            .Take(SuggestionLimit)
            .Select(p => new CatalogSuggestionDto(p.Id, p.Name, p.Price, p.ImageFileName, p.Availability.ToString()))
            .ToList();

        // Kategorie, jejichž název obsahuje všechna slova („note“ → Notebooky) – odkaz rovnou na filtr.
        var categories = (await GetCategoriesAsync(cancellationToken))
            .Where(c => words.All(word => CatalogRelevance.Match(CatalogSearchText.Normalize(c.Name), word) >= 2))
            .Take(SuggestedCategoryLimit)
            .ToList();

        TraceTags.Tag("minishop.search.results", found.Count);
        TraceTags.Tag("minishop.search.similar", isSimilar);
        return new CatalogSuggestions(categories, products, isSimilar);
    }

    public async Task<CatalogItemDto?> GetProductAsync(int productId, CancellationToken cancellationToken = default) =>
        await catalog.GetByIdAsync(productId, cancellationToken) is { IsRemoved: false } product ? product.ToCatalogItemDto() : null;

    /// <summary>
    /// Přesné hledání; když nic nenajde, podobná slova. Vrací i body relevance (přesné: <see cref="CatalogRelevance"/>,
    /// podobné: podobnost z databáze).
    /// </summary>
    private async Task<(IReadOnlyList<CatalogProduct> Found, Dictionary<int, double> Scores, bool IsSimilar)> FindAsync(
        IReadOnlyList<string> words, int? categoryId, bool inStockOnly, CancellationToken cancellationToken)
    {
        var exact = await catalog.SearchAsync(words, categoryId, inStockOnly, cancellationToken);
        if (exact.Count > 0 || words.Count == 0)
        {
            return (exact, exact.ToDictionary(p => p.Id, p => CatalogRelevance.Score(p, words)), false);
        }

        var similar = await catalog.SearchSimilarAsync(words, categoryId, inStockOnly, SimilarityThreshold, SimilarResultLimit,
            cancellationToken);
        return (similar.Select(s => s.Product).ToList(), similar.ToDictionary(s => s.Product.Id, s => s.Similarity), similar.Count > 0);
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
