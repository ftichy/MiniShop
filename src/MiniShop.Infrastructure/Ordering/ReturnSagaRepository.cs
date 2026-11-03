using Microsoft.EntityFrameworkCore;
using MiniShop.Application.Ordering;
using MiniShop.Domain.Orders.Saga;

namespace MiniShop.Infrastructure.Ordering;

/// <summary>E-shop krok 12: ságy vrácení zboží.</summary>
public sealed class ReturnSagaRepository(OrderingDbContext db) : IReturnSagaRepository
{
    private static readonly ReturnSagaState[] OpenStates =
        [ReturnSagaState.AwaitingGoods, ReturnSagaState.Restocking, ReturnSagaState.Refunding];

    public Task<ReturnSaga?> GetAsync(int orderId, CancellationToken cancellationToken = default) =>
        db.ReturnSagas.FirstOrDefaultAsync(s => s.OrderId == orderId, cancellationToken);

    public async Task<IReadOnlyList<int>> GetExpiredOrderIdsAsync(DateTime nowUtc, CancellationToken cancellationToken = default) =>
        await db.ReturnSagas
            .Where(s => s.State == ReturnSagaState.AwaitingGoods && s.GoodsDeadlineUtc <= nowUtc)
            .Select(s => s.OrderId)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<ReturnSaga>> GetOpenAsync(CancellationToken cancellationToken = default) =>
        await db.ReturnSagas
            .AsNoTracking()
            .Where(s => OpenStates.Contains(s.State))
            .OrderBy(s => s.OrderId)
            .ToListAsync(cancellationToken);

    public void Add(ReturnSaga saga) => db.ReturnSagas.Add(saga);
}
