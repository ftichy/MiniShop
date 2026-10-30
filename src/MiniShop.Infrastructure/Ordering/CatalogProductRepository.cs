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

    public async Task<IReadOnlyList<CatalogProduct>> GetAvailableByIdsAsync(IEnumerable<int> ids, CancellationToken cancellationToken = default)
    {
        var idList = ids.Distinct().ToList();
        return await db.CatalogProducts.Where(p => idList.Contains(p.Id) && !p.IsRemoved).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<CatalogProduct>> SearchAsync(string searchText, int? categoryId, bool inStockOnly,
        CancellationToken cancellationToken = default)
    {
        var query = db.CatalogProducts.Where(p => !p.IsRemoved);
        if (searchText.Length > 0)
        {
            query = query.Where(p => p.SearchText.Contains(searchText));   // SQL: instr(SearchText, @text) > 0
        }

        if (categoryId is not null)
        {
            query = query.Where(p => p.CategoryId == categoryId);
        }

        if (inStockOnly)
        {
            query = query.Where(p => p.Availability == CatalogAvailability.InStock || p.Availability == CatalogAvailability.LowStock);
        }

        return await query.ToListAsync(cancellationToken);
    }

    public void Add(CatalogProduct product) => db.CatalogProducts.Add(product);
}
