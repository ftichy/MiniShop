using Microsoft.EntityFrameworkCore;
using MiniShop.Application.Abstractions;
using MiniShop.Domain.Orders.Saga;
using MiniShop.Infrastructure.Persistence;

namespace MiniShop.Infrastructure.Repositories;

public sealed class OrderSagaRepository(MiniShopDbContext db) : IOrderSagaRepository
{
    public Task<OrderSaga?> GetAsync(int orderId, CancellationToken cancellationToken = default) =>
        db.OrderSagas.FirstOrDefaultAsync(s => s.OrderId == orderId, cancellationToken);

    public async Task<IReadOnlyList<int>> GetExpiredOrderIdsAsync(DateTime nowUtc, CancellationToken cancellationToken = default) =>
        await db.OrderSagas
            .Where(s => s.State == OrderSagaState.AwaitingReservation && s.ReservationDeadlineUtc <= nowUtc)
            .Select(s => s.OrderId)
            .ToListAsync(cancellationToken);

    public void Add(OrderSaga saga) => db.OrderSagas.Add(saga);
}
