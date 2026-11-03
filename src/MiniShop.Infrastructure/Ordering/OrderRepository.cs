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
                o.ShippingPrice,   // e-shop krok 4: útrata včetně dopravy
                Lines = o.Items.Select(i => new { i.UnitPrice, i.Quantity, i.Discount }).ToList()
            })
            .ToListAsync(cancellationToken);

        return rows
            .GroupBy(row => row.CustomerId)
            .Select(group => new CustomerOrderSummaryDto(
                group.Key,
                group.Count(),
                group.Where(row => row.Status is OrderStatus.Confirmed or OrderStatus.Completed)
                    .Sum(row => row.ShippingPrice + row.Lines.Sum(line => line.UnitPrice * line.Quantity - line.Discount))))
            .ToList();
    }

    // E-shop krok 5: jedna objednávka i s historií stavů (detail). Seznam historii nenačítá – nepotřebuje ji
    // a nový řádek historie EF uloží i bez načtení starých.
    public Task<Order?> GetByIdAsync(int id, CancellationToken cancellationToken = default) =>
        db.Orders
            .Include(o => o.Items)
            .Include(o => o.StatusHistory)
            .AsSplitQuery()
            .FirstOrDefaultAsync(o => o.Id == id, cancellationToken);

    // E-shop krok 6: fronty expedice – WHERE Status IN (…), nejstarší objednávka první (kdo dřív objednal, dřív dostane).
    public async Task<IReadOnlyList<Order>> GetByStatusesAsync(IReadOnlyCollection<OrderStatus> statuses,
        CancellationToken cancellationToken = default) =>
        await db.Orders
            .Include(o => o.Items)
            .Where(o => statuses.Contains(o.Status))
            .OrderBy(o => o.Id)
            .ToListAsync(cancellationToken);

    public Task<Order?> GetByCheckoutIdAsync(string customerId, Guid checkoutId, CancellationToken cancellationToken = default) =>
        db.Orders
            .Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.CheckoutId == checkoutId && o.CustomerId == customerId, cancellationToken);

    public async Task<DeliveryAddress?> GetLastDeliveryAddressAsync(string customerId, CancellationToken cancellationToken = default)
    {
        var last = await db.Orders
            .AsNoTracking()
            .Where(o => o.CustomerId == customerId && o.DeliveryAddress != null)
            .OrderByDescending(o => o.Id)
            .FirstOrDefaultAsync(cancellationToken);
        return last?.DeliveryAddress;
    }

    public void Add(Order order) => db.Orders.Add(order);
}
