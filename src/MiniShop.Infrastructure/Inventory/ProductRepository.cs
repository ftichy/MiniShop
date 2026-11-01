using Microsoft.EntityFrameworkCore;
using MiniShop.Application.Inventory;
using MiniShop.Domain.Inventory;

namespace MiniShop.Infrastructure.Inventory;

/// <remarks>E-shop krok 2: produkt se načítá vždy i s kategorií – její jméno jde ve zprávě ProductUpdatedV2.</remarks>
public sealed class ProductRepository(InventoryDbContext db) : IProductRepository
{
    private IQueryable<Product> Products => db.Products.Include(p => p.Category);

    public async Task<IReadOnlyList<Product>> GetAllAsync(CancellationToken cancellationToken = default) =>
        await Products.OrderBy(p => p.Name).ToListAsync(cancellationToken);

    public Task<Product?> GetByIdAsync(int id, CancellationToken cancellationToken = default) =>
        Products.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Product>> GetByIdsAsync(IEnumerable<int> ids, CancellationToken cancellationToken = default)
    {
        var idList = ids.Distinct().ToList();
        return await Products.Where(p => idList.Contains(p.Id)).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Product>> GetByCategoryAsync(int categoryId, CancellationToken cancellationToken = default) =>
        await Products.Where(p => p.CategoryId == categoryId).ToListAsync(cancellationToken);

    public void Add(Product product) => db.Products.Add(product);

    public void Remove(Product product) => db.Products.Remove(product);
}
