using Microsoft.EntityFrameworkCore;
using MiniShop.Application.Ordering;
using MiniShop.Domain.Orders.Saga;

namespace MiniShop.Infrastructure.Ordering;

/// <summary>E-shop krok 12: ságy vrácení zboží (krok 22: i vratky po částech).</summary>
public sealed class ReturnSagaRepository(OrderingDbContext db) : IReturnSagaRepository
{
    private static readonly ReturnSagaState[] OpenStates =
        [ReturnSagaState.AwaitingGoods, ReturnSagaState.Restocking, ReturnSagaState.Refunding];

    public Task<ReturnSaga?> GetAsync(int orderId, int returnNumber = ReturnSaga.WholeOrder, CancellationToken cancellationToken = default) =>
        db.ReturnSagas.FirstOrDefaultAsync(s => s.OrderId == orderId && s.ReturnNumber == returnNumber, cancellationToken);

    public async Task<IReadOnlyList<ReturnSagaKey>> GetExpiredAsync(DateTime nowUtc, CancellationToken cancellationToken = default) =>
        await db.ReturnSagas
            .Where(s => s.State == ReturnSagaState.AwaitingGoods && s.GoodsDeadlineUtc <= nowUtc)
            .Select(s => new ReturnSagaKey(s.OrderId, s.ReturnNumber))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<ReturnSaga>> GetOpenAsync(CancellationToken cancellationToken = default) =>
        await db.ReturnSagas
            .AsNoTracking()
            .Where(s => OpenStates.Contains(s.State))
            .OrderBy(s => s.OrderId).ThenBy(s => s.ReturnNumber)
            .ToListAsync(cancellationToken);

    public void Add(ReturnSaga saga) => db.ReturnSagas.Add(saga);
}
