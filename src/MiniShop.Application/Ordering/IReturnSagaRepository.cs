using MiniShop.Domain.Orders.Saga;

namespace MiniShop.Application.Ordering;

/// <summary>E-shop krok 12: ságy vrácení zboží. Krok 22: klíč = objednávka + číslo vratky (0 = celá objednávka).</summary>
public interface IReturnSagaRepository
{
    Task<ReturnSaga?> GetAsync(int orderId, int returnNumber = ReturnSaga.WholeOrder, CancellationToken cancellationToken = default);

    /// <summary>Vratky, u kterých zboží nedorazilo do termínu – kandidáti na timeout.</summary>
    Task<IReadOnlyList<ReturnSagaKey>> GetExpiredAsync(DateTime nowUtc, CancellationToken cancellationToken = default);

    /// <summary>Rozpracované vratky (čekají na zboží, naskladnění nebo vrácení peněz), nejstarší první – stránka Vratky.</summary>
    Task<IReadOnlyList<ReturnSaga>> GetOpenAsync(CancellationToken cancellationToken = default);

    void Add(ReturnSaga saga);
}

/// <summary>E-shop krok 22: klíč ságy vratky.</summary>
public sealed record ReturnSagaKey(int OrderId, int ReturnNumber);
