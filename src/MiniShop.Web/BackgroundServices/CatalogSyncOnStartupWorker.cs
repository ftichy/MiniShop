using MiniShop.Application.Ordering.Catalog;

namespace MiniShop.Web.BackgroundServices;

/// <summary>
/// Jednorázová kontrola po startu: když je kopie katalogu prázdná (např. nová ordering.db) nebo neúplná (e-shop krok 2:
/// produkty bez dostupnosti po nasazení výlohy), požádá sklad
/// o celý katalog (<see cref="CatalogSyncService"/>). Bez ní by formulář objednávky zůstal bez produktů,
/// dokud by se ve skladu nezměnil každý produkt.
/// </summary>
public sealed class CatalogSyncOnStartupWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<CatalogSyncOnStartupWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Nebrzdit start aplikace.
        await Task.Yield();

        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            if (await scope.ServiceProvider.GetRequiredService<CatalogSyncService>().RequestIfEmptyOrIncompleteAsync(stoppingToken))
            {
                logger.LogInformation("[Katalog] Kopie katalogu je prázdná nebo neúplná – žádám sklad o celý katalog (SyncCatalogV1)");
            }
        }
        catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
        {
            // Jen zápis do outboxu v ordering.db – selže leda databáze. Katalog jde obnovit i ručně na stránce Objednávky.
            logger.LogError(ex, "[Katalog] Žádost o obnovu katalogu se nepodařilo uložit");
        }
    }
}
