using Microsoft.EntityFrameworkCore;
using MiniShop.Application.Ordering;
using MiniShop.Domain.Orders;

namespace MiniShop.Infrastructure.Ordering;

public sealed class OrderRepository(OrderingDbContext db) : IOrderRepository
{
    public async Task<IReadOnlyList<Order>> GetAllAsync(string? customerId, CancellationToken cancellationToken = default) =>
        await db.Orders
            .Include(o => o.Items)
            .Where(o => customerId == null || o.CustomerId == customerId)   // účty krok 3: WHERE CustomerId = …
            .OrderByDescending(o => o.Id)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<CustomerOrderSummaryDto>> GetCustomerSummariesAsync(CancellationToken cancellationToken = default)
    {
        // SQLite neumí SUM nad decimal (ukládá ho jako text) – načteme jen potřebné sloupce a sečteme v paměti.
        var rows = await db.Orders
            .AsNoTracking()
            .Select(o => new
            {
                o.CustomerId,
                o.Status,
                Lines = o.Items.Select(i => new { i.UnitPrice, i.Quantity, i.Discount }).ToList()
            })
            .ToListAsync(cancellationToken);

        return rows
            .GroupBy(row => row.CustomerId)
            .Select(group => new CustomerOrderSummaryDto(
                group.Key,
                group.Count(),
                group.Where(row => row.Status is OrderStatus.Confirmed or OrderStatus.Completed)
                    .SelectMany(row => row.Lines)
                    .Sum(line => line.UnitPrice * line.Quantity - line.Discount)))
            .ToList();
    }

    public Task<Order?> GetByIdAsync(int id, CancellationToken cancellationToken = default) =>
        db.Orders
            .Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.Id == id, cancellationToken);

    public void Add(Order order) => db.Orders.Add(order);
}
