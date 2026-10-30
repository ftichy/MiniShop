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

    /// <summary>Produkty v nabídce (bez odebraných).</summary>
    Task<IReadOnlyList<CatalogProduct>> GetAvailableByIdsAsync(IEnumerable<int> ids, CancellationToken cancellationToken = default);

    /// <summary>
    /// E-shop krok 2: hledání ve výloze (bez odebraných). Filtruje databáze; řadí volající (SQLite neřadí decimal).
    /// </summary>
    /// <param name="searchText">Už normalizovaný text (<see cref="CatalogSearchText"/>); prázdný = bez hledání.</param>
    Task<IReadOnlyList<CatalogProduct>> SearchAsync(string searchText, int? categoryId, bool inStockOnly,
        CancellationToken cancellationToken = default);

    void Add(CatalogProduct product);
}
