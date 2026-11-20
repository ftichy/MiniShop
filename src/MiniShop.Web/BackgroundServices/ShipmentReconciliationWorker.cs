using MiniShop.Application.Shipping;
using MiniShop.Infrastructure.Observability;

namespace MiniShop.Web.BackgroundServices;

/// <summary>
/// E-shop krok 26C: dohledání zásilek – na webhook se nedá spolehnout na 100 % (dopravce ho může ztratit nebo vzdát).
/// Každou minutu se u zásilek na cestě bez novinky déle než <c>Shipping:ReconcileAfterMinutes</c> (2) zeptá dopravce
/// (GET) a zapíše, co chybí – stejnou cestou jako webhook. Běží jen na vedoucí kopii Webu.
/// </summary>
/// <remarks>
/// Přirovnání: SMS o balíku nepřišla – po chvíli se podíváte do sledování zásilky sami. Výpadek dopravce = jen log,
/// další kolo to zkusí znovu.
/// </remarks>
public sealed class ShipmentReconciliationWorker(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<ShipmentReconciliationWorker> logger) : BackgroundService
{
    private const int BatchSize = 20;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield();

        var quietFor = TimeSpan.FromMinutes(configuration.GetValue("Shipping:ReconcileAfterMinutes", 2.0));
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(configuration.GetValue("Shipping:ReconcileEveryMinutes", 1.0)));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            // Nevzniká z HTTP požadavku – vlastní trasa, na kterou naváže případné ShipmentDeliveredV1.
            using var activity = MessagingTelemetry.ActivitySource.StartActivity("shipment reconciliation");
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var checkedCount = await scope.ServiceProvider.GetRequiredService<ShipmentTrackingService>()
                    .ReconcileAsync(quietFor, BatchSize, stoppingToken);
                activity?.SetTag("minishop.shipment.reconciled", checkedCount);
                if (checkedCount > 0)
                {
                    logger.LogInformation("[Doprava] Dohledáno {Count} zásilek bez novinky od dopravce", checkedCount);
                }
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                activity.RecordError(ex);
                logger.LogWarning("[Doprava] Dohledání zásilek selhalo, zkusí se v dalším kole: {Error}", ex.Message);
            }
        }
    }
}
