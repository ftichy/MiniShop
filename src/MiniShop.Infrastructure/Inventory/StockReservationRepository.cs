using Microsoft.EntityFrameworkCore;
using MiniShop.Application.Inventory;
using MiniShop.Domain.Inventory;

namespace MiniShop.Infrastructure.Inventory;

public sealed class StockReservationRepository(InventoryDbContext db) : IStockReservationRepository
{
    public async Task<IReadOnlyList<StockReservation>> GetActiveByOrderIdAsync(int orderId, CancellationToken cancellationToken = default) =>
        await db.StockReservations
            .Where(r => r.OrderId == orderId && r.ReleasedAtUtc == null)
            .ToListAsync(cancellationToken);

    public void Add(StockReservation reservation) => db.StockReservations.Add(reservation);
}
