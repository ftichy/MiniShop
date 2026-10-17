using MiniShop.Infrastructure.Outbox;

namespace MiniShop.Web.BackgroundServices;

/// <summary>
/// Proces na pozadí (krok 5): každé 2 sekundy zavolá <see cref="OutboxProcessor"/>, aby doručil čekající události.
/// Běží po celou dobu života aplikace. Je tady ve Webu, protože tady už je ASP.NET hosting
/// (BackgroundService) – infrastruktura tak nepotřebuje další NuGet balíček.
/// </summary>
/// <remarks>
/// V každém kole se zpracuje jedna dávka. Zpráva, která selhala, se tak zkusí znovu nejdřív za 2 sekundy –
/// handler nedostane pět pokusů těsně po sobě.
/// </remarks>
public sealed class OutboxWorker(OutboxProcessor processor, ILogger<OutboxWorker> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(2);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Nebrzdit start aplikace – zbytek poběží až po spuštění hostu.
        await Task.Yield();

        using var timer = new PeriodicTimer(Interval);

        // První kolo hned po startu: dozpracuje zprávy, které zůstaly z doby před restartem.
        do
        {
            try
            {
                await processor.ProcessPendingAsync(stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                // Např. nedostupná DB. Worker nesmí spadnout – zkusí to v dalším kole.
                logger.LogError(ex, "Zpracování outboxu selhalo");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
