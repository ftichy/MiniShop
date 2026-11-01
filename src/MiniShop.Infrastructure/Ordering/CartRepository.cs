using Microsoft.EntityFrameworkCore;
using MiniShop.Application.Ordering.Carts;
using MiniShop.Domain.Orders.Carts;

namespace MiniShop.Infrastructure.Ordering;

public sealed class CartRepository(OrderingDbContext db) : ICartRepository
{
    public Task<Cart?> GetByCustomerIdAsync(string customerId, CancellationToken cancellationToken = default) =>
        db.Carts
            .Include(c => c.Lines)
            .FirstOrDefaultAsync(c => c.CustomerId == customerId, cancellationToken);

    public Task<int> GetTotalQuantityAsync(string customerId, CancellationToken cancellationToken = default) =>
        db.Carts
            .Where(c => c.CustomerId == customerId)
            .SelectMany(c => c.Lines)
            .SumAsync(l => l.Quantity, cancellationToken);

    public void Add(Cart cart) => db.Carts.Add(cart);

    public void Remove(Cart cart) => db.Carts.Remove(cart);
}
