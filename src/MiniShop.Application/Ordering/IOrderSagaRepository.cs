using MiniShop.Domain.Orders.Saga;

namespace MiniShop.Application.Ordering;

/// <summary>Uložený stav ság objednávek (krok 9).</summary>
public interface IOrderSagaRepository
{
    Task<OrderSaga?> GetAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>Ságy, které čekají na sklad déle, než je povoleno – kandidáti na timeout.</summary>
    Task<IReadOnlyList<int>> GetExpiredOrderIdsAsync(DateTime nowUtc, CancellationToken cancellationToken = default);

    void Add(OrderSaga saga);
}
