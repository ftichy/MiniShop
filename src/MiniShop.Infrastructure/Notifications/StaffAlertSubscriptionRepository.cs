using Microsoft.EntityFrameworkCore;
using MiniShop.Application.Notifications;
using MiniShop.Domain.Notifications;

namespace MiniShop.Infrastructure.Notifications;

public sealed class StaffAlertSubscriptionRepository(NotificationsDbContext db) : IStaffAlertSubscriptionRepository
{
    public async Task<IReadOnlyList<StaffAlertSubscription>> ListForUserAsync(string userId, CancellationToken cancellationToken = default) =>
        await db.StaffAlertSubscriptions.Where(s => s.UserId == userId).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<string>> ListUserIdsAsync(StaffAlertKind kind, CancellationToken cancellationToken = default) =>
        await db.StaffAlertSubscriptions.Where(s => s.Kind == kind).OrderBy(s => s.Id).Select(s => s.UserId).ToListAsync(cancellationToken);

    public void Add(StaffAlertSubscription subscription) => db.StaffAlertSubscriptions.Add(subscription);

    public void Remove(StaffAlertSubscription subscription) => db.StaffAlertSubscriptions.Remove(subscription);

    public async Task<int> RemoveForUserAsync(string userId, CancellationToken cancellationToken = default)
    {
        var forUser = await db.StaffAlertSubscriptions.Where(s => s.UserId == userId).ToListAsync(cancellationToken);
        db.StaffAlertSubscriptions.RemoveRange(forUser);
        return forUser.Count;
    }
}
