using MiniShop.Application.Ordering;
using MiniShop.Application.Ordering.Saga;

namespace MiniShop.Web.BackgroundServices;

/// <summary>
/// Hlídač termínů ságy (krok 9): každých 5 s najde objednávky, na které sklad neodpověděl včas,
/// a nechá orchestrátor je zamítnout. Kdyby rezervace dorazila později, orchestrátor ji uvolní (kompenzace).
/// </summary>
/// <remarks>
/// Každá sága se vyřizuje ve vlastním scope (vlastní DbContext) – chyba u jedné nezablokuje ostatní.
/// Když ságu mezitím změnila odpověď skladu, timeout ji nechá být (stav už není AwaitingReservation)
/// nebo narazí na concurrency token a zkusí to v dalším kole.
/// </remarks>
public sealed class OrderSagaTimeoutWorker(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    ILogger<OrderSagaTimeoutWorker> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield();

        using var timer = new PeriodicTimer(Interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await CheckTimeoutsAsync(stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError(ex, "Kontrola timeoutů ságy selhala");
            }
        }
    }

    private async Task CheckTimeoutsAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<int> expired;
        await using (var scope = scopeFactory.CreateAsyncScope())
        {
            expired = await scope.ServiceProvider.GetRequiredService<IOrderSagaRepository>()
                .GetExpiredOrderIdsAsync(timeProvider.GetUtcNow().UtcDateTime, cancellationToken);
        }

        foreach (var orderId in expired)
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                if (await scope.ServiceProvider.GetRequiredService<OrderSagaOrchestrator>().TimeOutAsync(orderId, cancellationToken))
                {
                    logger.LogWarning("[Sága] Objednávka {OrderId}: sklad neodpověděl včas, objednávka zamítnuta", orderId);
                }
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                logger.LogWarning("[Sága] Timeout objednávky {OrderId} se nepodařilo zpracovat, zkusí se znovu: {Error}",
                    orderId, ex.Message);
            }
        }
    }
}
