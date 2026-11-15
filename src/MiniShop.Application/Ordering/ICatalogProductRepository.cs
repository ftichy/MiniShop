using MiniShop.Domain.Orders;

namespace MiniShop.Application.Ordering;

/// <summary>Lokální kopie katalogu v modulu Ordering (read model, krok 10).</summary>
public interface ICatalogProductRepository
{
    Task<CatalogProduct?> GetByIdAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>Je v kopii katalogu vůbec něco (i odebrané produkty)? Prázdná = je potřeba ji obnovit ze skladu.</summary>
    Task<bool> AnyAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// E-shop krok 2: má produkt v nabídce, o kterém kopie neví všechno (dostupnost)? Typicky produkty z doby
    /// před krokem 2 – po nasazení je doplní obnova katalogu.
    /// </summary>
    Task<bool> AnyIncompleteAsync(CancellationToken cancellationToken = default);

    /// <summary>Celá nabídka (bez odebraných), seřazená podle názvu.</summary>
    Task<IReadOnlyList<CatalogProduct>> GetAvailableAsync(CancellationToken cancellationToken = default);

    /// <summary>E-shop krok 3: produkty podle ID včetně odebraných (košík u nich ukáže „už není v nabídce“).</summary>
    Task<IReadOnlyList<CatalogProduct>> GetByIdsAsync(IEnumerable<int> ids, CancellationToken cancellationToken = default);

    /// <summary>Produkty v nabídce (bez odebraných).</summary>
    Task<IReadOnlyList<CatalogProduct>> GetAvailableByIdsAsync(IEnumerable<int> ids, CancellationToken cancellationToken = default);

    /// <summary>
    /// E-shop krok 2: hledání ve výloze (bez odebraných). Filtruje databáze; řadí volající (z doby SQLite, které neřadilo decimal).
    /// E-shop krok 21: každé slovo musí být v textu produktu (<see cref="CatalogProduct.SearchText"/>), v libovolném pořadí.
    /// </summary>
    /// <param name="words">Slova z <see cref="CatalogSearchText.Words"/>; prázdné = bez hledání.</param>
    Task<IReadOnlyList<CatalogProduct>> SearchAsync(IReadOnlyList<string> words, int? categoryId, bool inStockOnly,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// E-shop krok 21: hledání s překlepy – každé slovo je v textu přesně, <b>nebo</b> se podobá některému slovu textu
    /// aspoň na <paramref name="threshold"/> (0–1, PostgreSQL pg_trgm <c>word_similarity</c>). Nejpodobnější první.
    /// </summary>
    Task<IReadOnlyList<CatalogSimilarProduct>> SearchSimilarAsync(IReadOnlyList<string> words, int? categoryId, bool inStockOnly,
        double threshold, int limit, CancellationToken cancellationToken = default);

    void Add(CatalogProduct product);
}

/// <summary>E-shop krok 21: produkt nalezený podle podobnosti a jak moc se dotazu podobá (0–1).</summary>
public sealed record CatalogSimilarProduct(CatalogProduct Product, double Similarity);
