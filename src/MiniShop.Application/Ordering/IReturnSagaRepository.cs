using MiniShop.Domain.Orders.Saga;

namespace MiniShop.Application.Ordering;

/// <summary>E-shop krok 12: ságy vrácení zboží v ordering.db.</summary>
public interface IReturnSagaRepository
{
    Task<ReturnSaga?> GetAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>Vratky, u kterých zboží nedorazilo do termínu – kandidáti na timeout.</summary>
    Task<IReadOnlyList<int>> GetExpiredOrderIdsAsync(DateTime nowUtc, CancellationToken cancellationToken = default);

    /// <summary>Rozpracované vratky (čekají na zboží, naskladnění nebo vrácení peněz), nejstarší první – stránka Vratky.</summary>
    Task<IReadOnlyList<ReturnSaga>> GetOpenAsync(CancellationToken cancellationToken = default);

    void Add(ReturnSaga saga);
}
