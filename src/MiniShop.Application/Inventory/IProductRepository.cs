using MiniShop.Domain.Inventory;

namespace MiniShop.Application.Inventory;

public interface IProductRepository
{
    Task<IReadOnlyList<Product>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<Product?> GetByIdAsync(int id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Product>> GetByIdsAsync(IEnumerable<int> ids, CancellationToken cancellationToken = default);

    /// <summary>E-shop krok 2: produkty jedné kategorie (přejmenování kategorie, kontrola před smazáním).</summary>
    Task<IReadOnlyList<Product>> GetByCategoryAsync(int categoryId, CancellationToken cancellationToken = default);

    void Add(Product product);
    void Remove(Product product);
}
