using Microsoft.EntityFrameworkCore;
using MiniShop.Application.Notifications;
using MiniShop.Domain.Notifications;

namespace MiniShop.Infrastructure.Notifications;

public sealed class OrderRecipientRepository(NotificationsDbContext db) : IOrderRecipientRepository
{
    public Task<OrderRecipient?> GetAsync(int orderId, CancellationToken cancellationToken = default) =>
        db.OrderRecipients.FirstOrDefaultAsync(r => r.OrderId == orderId, cancellationToken);

    public void Add(OrderRecipient recipient) => db.OrderRecipients.Add(recipient);

    public async Task<int> RemoveForCustomerAsync(string customerId, CancellationToken cancellationToken = default)
    {
        // Načíst a odebrat přes EF (ne ExecuteDelete) – smaže i vlastněné položky a uloží se v transakci inboxu.
        var forCustomer = await db.OrderRecipients.Where(r => r.CustomerId == customerId).ToListAsync(cancellationToken);
        db.OrderRecipients.RemoveRange(forCustomer);
        return forCustomer.Count;
    }
}
