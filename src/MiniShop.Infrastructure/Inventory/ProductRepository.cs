using Microsoft.EntityFrameworkCore;
using MiniShop.Application.Inventory;
using MiniShop.Domain.Inventory;

namespace MiniShop.Infrastructure.Inventory;

public sealed class ProductRepository(InventoryDbContext db) : IProductRepository
{
    public async Task<IReadOnlyList<Product>> GetAllAsync(CancellationToken cancellationToken = default) =>
        await db.Products.OrderBy(p => p.Name).ToListAsync(cancellationToken);

    public Task<Product?> GetByIdAsync(int id, CancellationToken cancellationToken = default) =>
        db.Products.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Product>> GetByIdsAsync(IEnumerable<int> ids, CancellationToken cancellationToken = default)
    {
        var idList = ids.Distinct().ToList();
        return await db.Products.Where(p => idList.Contains(p.Id)).ToListAsync(cancellationToken);
    }

    public void Add(Product product) => db.Products.Add(product);

    public void Remove(Product product) => db.Products.Remove(product);
}
