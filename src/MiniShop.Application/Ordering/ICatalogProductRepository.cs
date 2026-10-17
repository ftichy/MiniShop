using MiniShop.Domain.Orders;

namespace MiniShop.Application.Ordering;

/// <summary>Lokální kopie katalogu v modulu Ordering (read model, krok 10).</summary>
public interface ICatalogProductRepository
{
    Task<CatalogProduct?> GetByIdAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>Produkty v nabídce (bez odebraných).</summary>
    Task<IReadOnlyList<CatalogProduct>> GetAvailableByIdsAsync(IEnumerable<int> ids, CancellationToken cancellationToken = default);

    void Add(CatalogProduct product);
}
