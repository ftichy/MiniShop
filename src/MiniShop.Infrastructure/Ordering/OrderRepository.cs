using Microsoft.EntityFrameworkCore;
using MiniShop.Application.Ordering;
using MiniShop.Domain.Orders;

namespace MiniShop.Infrastructure.Ordering;

public sealed class OrderRepository(OrderingDbContext db) : IOrderRepository
{
    public async Task<IReadOnlyList<Order>> GetAllAsync(CancellationToken cancellationToken = default) =>
        await db.Orders
            .Include(o => o.Items)
            .OrderByDescending(o => o.Id)
            .ToListAsync(cancellationToken);

    public Task<Order?> GetByIdAsync(int id, CancellationToken cancellationToken = default) =>
        db.Orders
            .Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.Id == id, cancellationToken);

    public void Add(Order order) => db.Orders.Add(order);
}
