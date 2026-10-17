using Microsoft.EntityFrameworkCore;
using MiniShop.Application.Abstractions;
using MiniShop.Domain.Inventory;
using MiniShop.Infrastructure.Persistence;

namespace MiniShop.Infrastructure.Repositories;

public sealed class StockReservationRepository(MiniShopDbContext db) : IStockReservationRepository
{
    public async Task<IReadOnlyList<StockReservation>> GetActiveByOrderIdAsync(int orderId, CancellationToken cancellationToken = default) =>
        await db.StockReservations
            .Where(r => r.OrderId == orderId && r.ReleasedAtUtc == null)
            .ToListAsync(cancellationToken);

    public void Add(StockReservation reservation) => db.StockReservations.Add(reservation);
}
