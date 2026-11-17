using Microsoft.EntityFrameworkCore;
using MiniShop.Application.Notifications;
using MiniShop.Domain.Notifications;

namespace MiniShop.Infrastructure.Notifications;

public sealed class PushSubscriptionRepository(NotificationsDbContext db) : IPushSubscriptionRepository
{
    public Task<PushSubscription?> FindByEndpointAsync(string endpoint, CancellationToken cancellationToken = default) =>
        db.PushSubscriptions.FirstOrDefaultAsync(s => s.Endpoint == endpoint, cancellationToken);

    public Task<PushSubscription?> FindAsync(int id, CancellationToken cancellationToken = default) =>
        db.PushSubscriptions.FirstOrDefaultAsync(s => s.Id == id, cancellationToken);

    public async Task<IReadOnlyList<PushSubscription>> ListForCustomerAsync(string customerId, CancellationToken cancellationToken = default) =>
        await db.PushSubscriptions.Where(s => s.CustomerId == customerId).OrderBy(s => s.Id).ToListAsync(cancellationToken);

    public void Add(PushSubscription subscription) => db.PushSubscriptions.Add(subscription);

    public void Remove(PushSubscription subscription) => db.PushSubscriptions.Remove(subscription);

    public async Task<int> RemoveForCustomerAsync(string customerId, CancellationToken cancellationToken = default)
    {
        var forCustomer = await db.PushSubscriptions.Where(s => s.CustomerId == customerId).ToListAsync(cancellationToken);
        db.PushSubscriptions.RemoveRange(forCustomer);
        return forCustomer.Count;
    }
}
