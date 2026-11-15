using Microsoft.EntityFrameworkCore;
using MiniShop.Application.Ordering;
using MiniShop.Domain.Orders;

namespace MiniShop.Infrastructure.Ordering;

public sealed class CatalogProductRepository(OrderingDbContext db) : ICatalogProductRepository
{
    public Task<CatalogProduct?> GetByIdAsync(int id, CancellationToken cancellationToken = default) =>
        db.CatalogProducts.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

    public Task<bool> AnyAsync(CancellationToken cancellationToken = default) =>
        db.CatalogProducts.AnyAsync(cancellationToken);

    public Task<bool> AnyIncompleteAsync(CancellationToken cancellationToken = default) =>
        db.CatalogProducts.AnyAsync(p => !p.IsRemoved && p.Availability == CatalogAvailability.Unknown, cancellationToken);

    public async Task<IReadOnlyList<CatalogProduct>> GetAvailableAsync(CancellationToken cancellationToken = default) =>
        await db.CatalogProducts.Where(p => !p.IsRemoved).OrderBy(p => p.Name).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<CatalogProduct>> GetByIdsAsync(IEnumerable<int> ids, CancellationToken cancellationToken = default)
    {
        var idList = ids.Distinct().ToList();
        return await db.CatalogProducts.Where(p => idList.Contains(p.Id)).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<CatalogProduct>> GetAvailableByIdsAsync(IEnumerable<int> ids, CancellationToken cancellationToken = default)
    {
        var idList = ids.Distinct().ToList();
        return await db.CatalogProducts.Where(p => idList.Contains(p.Id) && !p.IsRemoved).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<CatalogProduct>> SearchAsync(IReadOnlyList<string> words, int? categoryId, bool inStockOnly,
        CancellationToken cancellationToken = default)
    {
        var query = Filter(categoryId, inStockOnly);
        foreach (var word in words)
        {
            // E-shop krok 21: každé slovo zvlášť (AND) – SQL: "SearchText" LIKE '%slovo%' (zrychluje GIN index s pg_trgm).
            var pattern = ContainsPattern(word);
            query = query.Where(p => EF.Functions.Like(p.SearchText, pattern, LikeEscape));
        }

        return await query.ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<CatalogSimilarProduct>> SearchSimilarAsync(IReadOnlyList<string> words, int? categoryId,
        bool inStockOnly, double threshold, int limit, CancellationToken cancellationToken = default)
    {
        if (words.Count == 0)
        {
            return [];
        }

        var query = Filter(categoryId, inStockOnly);
        foreach (var word in words)
        {
            // Slovo je v textu přesně, nebo se podobá některému jeho slovu (pg_trgm word_similarity – trojice písmen).
            var pattern = ContainsPattern(word);
            query = query.Where(p => EF.Functions.Like(p.SearchText, pattern, LikeEscape)
                                     || EF.Functions.TrigramsWordSimilarity(word, p.SearchText) >= threshold);
        }

        // Pořadí podle podobnosti celého dotazu („notbuk 32“ ↔ text produktu).
        var phrase = string.Join(' ', words);
        var rows = await query
            .Select(p => new { Product = p, Similarity = EF.Functions.TrigramsWordSimilarity(phrase, p.SearchText) })
            .OrderByDescending(r => r.Similarity)
            .Take(limit)
            .ToListAsync(cancellationToken);

        return rows.Select(r => new CatalogSimilarProduct(r.Product, r.Similarity)).ToList();
    }

    private IQueryable<CatalogProduct> Filter(int? categoryId, bool inStockOnly)
    {
        var query = db.CatalogProducts.Where(p => !p.IsRemoved);
        if (categoryId is not null)
        {
            query = query.Where(p => p.CategoryId == categoryId);
        }

        if (inStockOnly)
        {
            query = query.Where(p => p.Availability == CatalogAvailability.InStock || p.Availability == CatalogAvailability.LowStock);
        }

        return query;
    }

    // Escape znak pro LIKE. Bez něj Npgsql napíše ESCAPE '' a zpětné lomítko by nic neznamenalo.
    private const string LikeEscape = @"\";

    /// <summary>„%slovo%“ pro LIKE – znaky %, _ a \ ze zákazníkova textu se berou doslova.</summary>
    internal static string ContainsPattern(string word) =>
        "%" + word.Replace(@"\", @"\\").Replace("%", @"\%").Replace("_", @"\_") + "%";

    public void Add(CatalogProduct product) => db.CatalogProducts.Add(product);
}
