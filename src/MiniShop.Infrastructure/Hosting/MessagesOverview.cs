using Microsoft.Extensions.Logging;
using MiniShop.Infrastructure.Integration.RabbitMq;
using MiniShop.Infrastructure.Outbox;

namespace MiniShop.Infrastructure.Hosting;

/// <summary>
/// Stav zpráv jedné služby: outboxy jejích modulů (čekající, poison) a dead-letter fronty v RabbitMQ.
/// </summary>
/// <param name="DeadLetters">Počty v <c>minishop.{modul}.dead</c>; null = RabbitMQ teď nejde zkontrolovat.</param>
public sealed record ServiceMessagesStatus(IReadOnlyList<MessageStoreStatus> Modules, IReadOnlyDictionary<string, uint>? DeadLetters);

/// <summary>Odpověď <c>POST /api/messages/{modul}/{id}/retry</c>.</summary>
public sealed record MessageRetryResult(bool Retried);

/// <summary>Odpověď <c>POST /api/messages/queues/{fronta}/requeue</c>.</summary>
public sealed record MessageRequeueResult(int Moved);

/// <summary>
/// EDA krok 13 + mikroservisy M2B: provoz zpráv JEDNÉ služby – přehled, „Zkusit znovu“ (poison z outboxu) a „Vrátit
/// do fronty“ (dead-letter). Dřív to uměla jen stránka /Admin/Messages pro moduly Webu; teď stejnou logiku nabízí každá
/// služba přes <c>/api/messages</c> (se svým servisním klíčem) a stránka ukáže všechny služby.
/// </summary>
public sealed class MessagesOverview(
    IEnumerable<IMessageStore> stores,
    RabbitMqDeadLetterService deadLetters,
    ILogger<MessagesOverview> logger)
{
    /// <summary>Nedostupný broker nesmí zdržet stránku – po 3 s se dead-letter fronty vynechají.</summary>
    private static readonly TimeSpan BrokerTimeout = TimeSpan.FromSeconds(3);

    public async Task<ServiceMessagesStatus> GetAsync(CancellationToken cancellationToken = default)
    {
        var modules = new List<MessageStoreStatus>();
        foreach (var store in stores.OrderBy(s => s.ModuleName, StringComparer.Ordinal))
        {
            modules.Add(await store.GetStatusAsync(cancellationToken));
        }

        IReadOnlyDictionary<string, uint>? dead = null;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(BrokerTimeout);
            dead = await deadLetters.GetCountsAsync(timeout.Token);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("[Zprávy] Dead-letter fronty nejde zkontrolovat: {Error}", ex.Message);
        }

        return new ServiceMessagesStatus(modules, dead);
    }

    /// <summary>Poison zpráva z outboxu dostane nové pokusy. False = modul tu není, nebo zpráva už nečeká.</summary>
    public async Task<bool> RetryAsync(string module, Guid messageId, CancellationToken cancellationToken = default) =>
        stores.FirstOrDefault(s => s.ModuleName == module) is { } store && await store.RetryAsync(messageId, cancellationToken);

    /// <summary>Zprávy z dead-letter fronty zpět do fronty modulu.</summary>
    /// <exception cref="ArgumentException">Fronta nepatří této službě.</exception>
    public Task<int> RequeueAsync(string queue, CancellationToken cancellationToken = default) =>
        deadLetters.RequeueAsync(queue, cancellationToken);
}
