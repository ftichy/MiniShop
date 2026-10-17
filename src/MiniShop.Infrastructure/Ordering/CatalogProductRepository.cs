using Microsoft.EntityFrameworkCore;
using MiniShop.Application.Ordering;
using MiniShop.Domain.Orders;

namespace MiniShop.Infrastructure.Ordering;

public sealed class CatalogProductRepository(OrderingDbContext db) : ICatalogProductRepository
{
    public Task<CatalogProduct?> GetByIdAsync(int id, CancellationToken cancellationToken = default) =>
        db.CatalogProducts.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

    public async Task<IReadOnlyList<CatalogProduct>> GetAvailableByIdsAsync(IEnumerable<int> ids, CancellationToken cancellationToken = default)
    {
        var idList = ids.Distinct().ToList();
        return await db.CatalogProducts.Where(p => idList.Contains(p.Id) && !p.IsRemoved).ToListAsync(cancellationToken);
    }

    public void Add(CatalogProduct product) => db.CatalogProducts.Add(product);
}
