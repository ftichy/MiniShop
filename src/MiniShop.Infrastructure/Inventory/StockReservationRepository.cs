using Microsoft.EntityFrameworkCore;
using MiniShop.Application.Inventory;
using MiniShop.Domain.Inventory;

namespace MiniShop.Infrastructure.Inventory;

public sealed class StockReservationRepository(InventoryDbContext db) : IStockReservationRepository
{
    public async Task<IReadOnlyList<StockReservation>> GetActiveByOrderIdAsync(int orderId, CancellationToken cancellationToken = default) =>
        await db.StockReservations
            .Where(r => r.OrderId == orderId && r.ReleasedAtUtc == null && r.FulfilledAtUtc == null)   // e-shop krok 6: ani vydané
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<StockReservation>> GetReturnableByOrderIdAsync(int orderId, CancellationToken cancellationToken = default) =>
        await db.StockReservations
            .Where(r => r.OrderId == orderId && r.FulfilledAtUtc != null && r.ReturnedAtUtc == null)   // e-shop krok 12
            .ToListAsync(cancellationToken);

    public Task<StockReturn?> GetReturnAsync(int orderId, int returnNumber, CancellationToken cancellationToken = default) =>
        db.StockReturns.FirstOrDefaultAsync(r => r.OrderId == orderId && r.ReturnNumber == returnNumber, cancellationToken);

    public void Add(StockReservation reservation) => db.StockReservations.Add(reservation);

    public void AddReturn(StockReturn stockReturn) => db.StockReturns.Add(stockReturn);
}
