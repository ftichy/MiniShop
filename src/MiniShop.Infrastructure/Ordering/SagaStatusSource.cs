using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MiniShop.Domain.Orders.Saga;
using MiniShop.Infrastructure.Observability;

namespace MiniShop.Infrastructure.Ordering;

/// <summary>
/// Observabilita G2: rozpracované ságy podle stavu (metrika <c>minishop.saga.active</c>, štítky <c>saga</c> = order/return
/// a <c>state</c>). Konečné stavy (Completed, Rejected, Released, Cancelled, Expired) se nehlásí – rostly by donekonečna
/// a nic neříkají. Stav, ve kterém sága visí dlouho (AwaitingReservation, ReleasingStock…), je signál pro alarm (G3).
/// </summary>
public sealed class SagaStatusSource(IServiceScopeFactory scopeFactory) : IOperationalMetricsSource
{
    /// <summary>Stavy ságy objednávky, které ještě nejsou u konce (Reserved/Shipped trvají až do expedice a doručení).</summary>
    public static readonly OrderSagaState[] ActiveOrderStates =
    [
        OrderSagaState.AwaitingReservation, OrderSagaState.AwaitingPayment, OrderSagaState.ReleasingStock,
        OrderSagaState.Reserved, OrderSagaState.Shipped
    ];

    /// <summary>Stavy ságy vratky, které ještě nejsou u konce.</summary>
    public static readonly ReturnSagaState[] ActiveReturnStates =
        [ReturnSagaState.AwaitingGoods, ReturnSagaState.Restocking, ReturnSagaState.Refunding];

    public string Name => "sagas";

    public async Task<IReadOnlyList<OperationalReading>> ReadAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OrderingDbContext>();

        var orders = await db.OrderSagas.AsNoTracking()
            .Where(saga => ActiveOrderStates.Contains(saga.State))
            .GroupBy(saga => saga.State)
            .Select(group => new { State = group.Key, Count = group.Count() })
            .ToListAsync(cancellationToken);
        var returns = await db.ReturnSagas.AsNoTracking()
            .Where(saga => ActiveReturnStates.Contains(saga.State))
            .GroupBy(saga => saga.State)
            .Select(group => new { State = group.Key, Count = group.Count() })
            .ToListAsync(cancellationToken);

        // Každý aktivní stav vždy (i s nulou) – v grafu je pak vidět „0“, ne díra.
        return ActiveOrderStates
            .Select(state => Reading("order", state.ToString(), orders.FirstOrDefault(o => o.State == state)?.Count ?? 0))
            .Concat(ActiveReturnStates
                .Select(state => Reading("return", state.ToString(), returns.FirstOrDefault(r => r.State == state)?.Count ?? 0)))
            .ToList();
    }

    private static OperationalReading Reading(string saga, string state, int count) =>
        new(OperationalMetrics.SagaActive, count,
            new KeyValuePair<string, object?>("saga", saga), new KeyValuePair<string, object?>("state", state));
}
