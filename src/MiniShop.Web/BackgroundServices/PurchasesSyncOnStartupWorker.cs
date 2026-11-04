using MiniShop.Application.Reviews;

namespace MiniShop.Web.BackgroundServices;

/// <summary>
/// E-shop krok 14: jednorázová kontrola po startu – prázdná kopie nákupů v modulu Reviews (nová reviews.db, první
/// nasazení) se doplní ze snímků objednávek (<see cref="ReviewService.SyncPurchasesIfEmptyAsync"/>). Díky tomu jdou
/// hodnotit i produkty z objednávek doručených dřív, než modul vznikl. Jako obnova katalogu.
/// </summary>
public sealed class PurchasesSyncOnStartupWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<PurchasesSyncOnStartupWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Nebrzdit start aplikace.
        await Task.Yield();

        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            if (await scope.ServiceProvider.GetRequiredService<ReviewService>().SyncPurchasesIfEmptyAsync(stoppingToken))
            {
                logger.LogInformation("[Hodnocení] Kopie nákupů je prázdná – žádám Ordering o snímky objednávek (SyncPurchasesV1)");
            }
        }
        catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
        {
            // Jen zápis do outboxu v reviews.db – selže leda databáze; při dalším startu se to zkusí znovu.
            logger.LogError(ex, "[Hodnocení] Žádost o snímky nákupů se nepodařilo uložit");
        }
    }
}
