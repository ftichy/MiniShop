using MiniShop.Application.Reporting;

namespace MiniShop.Web.BackgroundServices;

/// <summary>
/// E-shop krok 11: jednorázová kontrola po startu – prázdný přehled tržeb (nová reporting.db, první nasazení)
/// se postaví ze snímků objednávek (<see cref="SalesReportService.RebuildIfEmptyAsync"/>). Jako obnova katalogu.
/// </summary>
public sealed class SalesReportOnStartupWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<SalesReportOnStartupWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Nebrzdit start aplikace.
        await Task.Yield();

        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            if (await scope.ServiceProvider.GetRequiredService<SalesReportService>().RebuildIfEmptyAsync(stoppingToken))
            {
                logger.LogInformation("[Přehled] Přehled tržeb je prázdný – žádám Ordering o snímky objednávek (RebuildSalesReportV1)");
            }
        }
        catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
        {
            // Jen zápis do outboxu v reporting.db – přehled jde přestavět i tlačítkem na stránce Přehled.
            logger.LogError(ex, "[Přehled] Žádost o přestavění přehledu se nepodařilo uložit");
        }
    }
}
