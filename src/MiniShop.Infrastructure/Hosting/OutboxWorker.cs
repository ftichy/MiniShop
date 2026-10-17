using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MiniShop.Infrastructure.Outbox;

namespace MiniShop.Infrastructure.Hosting;

/// <summary>
/// Proces na pozadí (krok 5): každé 2 sekundy zavolá <see cref="OutboxProcessor{TDbContext}"/>, aby doručil čekající zprávy.
/// Běží po celou dobu života aplikace.
/// </summary>
/// <remarks>
/// V každém kole se zpracuje jedna dávka. Zpráva, která selhala, se tak zkusí znovu nejdřív za 2 sekundy –
/// handler nedostane pět pokusů těsně po sobě.
/// Krok 10B: worker je v Infrastructure, protože ho používají obě služby (Web i Inventory.Api).
/// Každá služba má zaregistrovaný jen outbox svého modulu, takže zpracovává jen svou databázi.
/// </remarks>
public sealed class OutboxWorker(IEnumerable<IOutboxProcessor> processors, ILogger<OutboxWorker> logger) : BackgroundService
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
                // Krok 10: každý modul má vlastní outbox – vybereme všechny schránky, které tahle služba má.
                foreach (var processor in processors)
                {
                    await processor.ProcessPendingAsync(stoppingToken);
                }
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
