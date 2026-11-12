using Microsoft.EntityFrameworkCore;
using MiniShop.Application.Notifications;
using MiniShop.Domain.Notifications;

namespace MiniShop.Infrastructure.Notifications;

public sealed class BackInStockRepository(NotificationsDbContext db) : IBackInStockRepository
{
    public Task<BackInStockSubscription?> FindAsync(string customerId, int productId, CancellationToken cancellationToken = default) =>
        db.BackInStockSubscriptions.FirstOrDefaultAsync(s => s.CustomerId == customerId && s.ProductId == productId, cancellationToken);

    public async Task<IReadOnlyList<BackInStockSubscription>> ListForProductAsync(int productId, CancellationToken cancellationToken = default) =>
        await db.BackInStockSubscriptions.Where(s => s.ProductId == productId).OrderBy(s => s.Id).ToListAsync(cancellationToken);

    public void Add(BackInStockSubscription subscription) => db.BackInStockSubscriptions.Add(subscription);

    public void Remove(BackInStockSubscription subscription) => db.BackInStockSubscriptions.Remove(subscription);

    public async Task<int> RemoveForCustomerAsync(string customerId, CancellationToken cancellationToken = default)
    {
        var forCustomer = await db.BackInStockSubscriptions.Where(s => s.CustomerId == customerId).ToListAsync(cancellationToken);
        db.BackInStockSubscriptions.RemoveRange(forCustomer);
        return forCustomer.Count;
    }
}
