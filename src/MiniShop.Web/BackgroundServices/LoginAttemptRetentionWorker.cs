using MiniShop.Infrastructure.Accounts;

namespace MiniShop.Web.BackgroundServices;

/// <summary>
/// 2FA krok B: úklid auditu přihlášení – pokusy starší než <see cref="LoginAttemptRecorder.Retention"/> (90 dní) se mažou,
/// protože obsahují IP adresu (osobní údaj). Běží jen na vedoucí kopii a po startu hned, pak každou hodinu.
/// </summary>
public sealed class LoginAttemptRetentionWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<LoginAttemptRetentionWorker> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield();

        using var timer = new PeriodicTimer(Interval);
        do
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var deleted = await scope.ServiceProvider.GetRequiredService<LoginAttemptRecorder>()
                    .PurgeExpiredAsync(stoppingToken);
                if (deleted > 0)
                {
                    logger.LogInformation("[Úklid] Smazáno {Count} starých pokusů o přihlášení", deleted);
                }
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogWarning("[Úklid] Úklid auditu přihlášení selhal, zkusí se za hodinu: {Error}", ex.Message);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
