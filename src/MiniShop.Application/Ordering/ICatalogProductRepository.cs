using MiniShop.Domain.Orders;

namespace MiniShop.Application.Ordering;

/// <summary>Lokální kopie katalogu v modulu Ordering (read model, krok 10).</summary>
public interface ICatalogProductRepository
{
    Task<CatalogProduct?> GetByIdAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>Je v kopii katalogu vůbec něco (i odebrané produkty)? Prázdná = je potřeba ji obnovit ze skladu.</summary>
    Task<bool> AnyAsync(CancellationToken cancellationToken = default);

    /// <summary>Celá nabídka (bez odebraných), seřazená podle názvu.</summary>
    Task<IReadOnlyList<CatalogProduct>> GetAvailableAsync(CancellationToken cancellationToken = default);

    /// <summary>Produkty v nabídce (bez odebraných).</summary>
    Task<IReadOnlyList<CatalogProduct>> GetAvailableByIdsAsync(IEnumerable<int> ids, CancellationToken cancellationToken = default);

    void Add(CatalogProduct product);
}
