using MiniShop.Application.Reporting;
using MiniShop.Infrastructure.Observability;

namespace MiniShop.Reporting.Api;

/// <summary>
/// E-shop krok 27A: každých <c>Recommendations:IntervalMinutes</c> (5) přepočítá „Zákazníci také koupili“
/// (<see cref="ProductRecommendationService.RecomputeAsync"/>) a změny pošle zprávou. Běží jen na vedoucí kopii služby.
/// </summary>
/// <remarks>První výpočet hned po startu (nová služba nečeká 5 minut). Chyba = jen log, další kolo to zkusí znovu.</remarks>
public sealed class ProductRecommendationsWorker(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<ProductRecommendationsWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield();

        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(configuration.GetValue("Recommendations:IntervalMinutes", 5.0)));
        do
        {
            // Nevzniká z HTTP požadavku – vlastní trasa, na kterou navážou odeslané zprávy.
            using var activity = MessagingTelemetry.ActivitySource.StartActivity("product recommendations");
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var changed = await scope.ServiceProvider.GetRequiredService<ProductRecommendationService>()
                    .RecomputeAsync(stoppingToken);
                if (changed > 0)
                {
                    logger.LogInformation("[Doporučení] Změnila se doporučení u {Count} produktů", changed);
                }
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                activity.RecordError(ex);
                logger.LogWarning("[Doporučení] Přepočet selhal, zkusí se v dalším kole: {Error}", ex.Message);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
