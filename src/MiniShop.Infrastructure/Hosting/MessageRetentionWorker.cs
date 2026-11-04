using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MiniShop.Infrastructure.Outbox;

namespace MiniShop.Infrastructure.Hosting;

/// <summary>
/// EDA krok 13: úklid „pošty“ – jednou za hodinu (a hned po startu) smaže v každém modulu zpracované zprávy outboxu
/// starší než <see cref="MessageRetentionSettings.OutboxRetention"/> a záznamy inboxu starší než
/// <see cref="MessageRetentionSettings.InboxRetention"/>. Bez úklidu by tabulky jen rostly (a s nimi zálohy).
/// </summary>
/// <remarks>
/// Nezpracované a poison zprávy nemaže nikdy. Běží ve Webu i v Inventory.Api – každá služba uklízí databáze svých modulů.
/// </remarks>
public sealed class MessageRetentionWorker(
    IEnumerable<IMessageStore> stores,
    MessageRetentionSettings settings,
    TimeProvider timeProvider,
    ILogger<MessageRetentionWorker> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield();

        using var timer = new PeriodicTimer(Interval);
        do
        {
            await CleanUpAsync(stoppingToken);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    /// <summary>Jedno kolo úklidu všech modulů (veřejné kvůli testům).</summary>
    public async Task CleanUpAsync(CancellationToken cancellationToken)
    {
        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        foreach (var store in stores)
        {
            try
            {
                var result = await store.CleanUpAsync(nowUtc - settings.OutboxRetention, nowUtc - settings.InboxRetention,
                    cancellationToken);
                if (result.OutboxDeleted + result.InboxDeleted > 0)
                {
                    logger.LogInformation("[Úklid] {Module}: smazáno {Outbox} zpráv outboxu a {Inbox} záznamů inboxu",
                        result.Module, result.OutboxDeleted, result.InboxDeleted);
                }
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                // Zamčená DB apod. – úklid počká na další kolo, provoz nezastaví.
                logger.LogWarning("[Úklid] {Module}: úklid zpráv selhal, zkusí se za hodinu: {Error}", store.ModuleName, ex.Message);
            }
        }
    }
}

/// <summary>EDA krok 13: jak dlouho držet zpracované zprávy (sekce Messaging:Retention).</summary>
/// <param name="OutboxRetention">Zpracované zprávy outboxu (výchozí 7 dní) – stačí na dohledání a ladění.</param>
/// <param name="InboxRetention">
/// Záznamy inboxu (výchozí 30 dní). Musí být delší než nejdelší možné opakované doručení – jinak by se smazaný
/// záznam „nepoznal“ a zpráva by se zpracovala podruhé.
/// </param>
public sealed record MessageRetentionSettings(TimeSpan OutboxRetention, TimeSpan InboxRetention);
