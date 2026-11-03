using MiniShop.Application.Ordering.Carts;
using MiniShop.Infrastructure.Observability;

namespace MiniShop.Web.BackgroundServices;

/// <summary>
/// E-shop krok 10: hlídač opuštěných košíků – každých <see cref="CartReminderSettings.CheckInterval"/> (15 min)
/// najde košíky, které se dlouho nezměnily, a nechá <see cref="CartReminderService"/> oznámit CartAbandonedV1.
/// </summary>
/// <remarks>
/// Stejný vzor jako <see cref="OrderSagaTimeoutWorker"/>: každý košík ve vlastním scope a vlastním spanu (krok 11 –
/// zpráva tak má trasu). Souběh se zákazníkem (změnil košík) = concurrency token → košík se přeskočí; v dalším kole
/// už opuštěný není.
/// </remarks>
public sealed class CartReminderWorker(
    IServiceScopeFactory scopeFactory,
    CartReminderSettings settings,
    ILogger<CartReminderWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield();

        using var timer = new PeriodicTimer(settings.CheckInterval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await CheckCartsAsync(stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError(ex, "Kontrola opuštěných košíků selhala");
            }
        }
    }

    private async Task CheckCartsAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<int> abandoned;
        await using (var scope = scopeFactory.CreateAsyncScope())
        {
            abandoned = await scope.ServiceProvider.GetRequiredService<CartReminderService>()
                .GetAbandonedCartIdsAsync(cancellationToken);
        }

        foreach (var cartId in abandoned)
        {
            using var activity = MessagingTelemetry.ActivitySource.StartActivity("cart reminder");
            activity?.SetTag("minishop.cart.id", cartId);

            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                if (await scope.ServiceProvider.GetRequiredService<CartReminderService>().RemindAsync(cartId, cancellationToken))
                {
                    logger.LogInformation("[Košík] Košík {CartId} je opuštěný – oznámeno CartAbandonedV1", cartId);
                }
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                activity.RecordError(ex);
                logger.LogWarning("[Košík] Připomínku košíku {CartId} se nepodařilo zpracovat, zkusí se znovu: {Error}",
                    cartId, ex.Message);
            }
        }
    }
}
