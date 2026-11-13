using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;

namespace MiniShop.Infrastructure.Integration.RabbitMq;

/// <summary>
/// EDA krok 13: dead-letter fronty modulů této služby – kolik v nich je zpráv a jejich vrácení zpět do fronty
/// (po opravě chyby). Používá ho stránka Zprávy pro admina a health check.
/// </summary>
public sealed class RabbitMqDeadLetterService(
    RabbitMqConnection connection,
    IEnumerable<IntegrationModule> modules,
    ILogger<RabbitMqDeadLetterService> logger)
{
    /// <summary>Nejvýš tolik zpráv se vrátí jedním kliknutím (zbytek dalším).</summary>
    public const int RequeueLimit = 500;

    /// <summary>Fronty modulů, které tahle služba poslouchá („minishop.ordering“…).</summary>
    public IReadOnlyList<string> Queues { get; } =
        modules.Select(m => $"minishop.{m.Name}").Distinct().Order(StringComparer.Ordinal).ToList();

    /// <summary>Počet zpráv v dead-letter frontě každé fronty. Fronta, která ještě neexistuje, má 0.</summary>
    public async Task<IReadOnlyDictionary<string, uint>> GetCountsAsync(CancellationToken cancellationToken = default)
    {
        var counts = new Dictionary<string, uint>();
        await using var channel = await connection.CreateChannelAsync(publisherConfirms: false, cancellationToken);
        foreach (var queue in Queues)
        {
            await RabbitMqDeadLetters.DeclareDeadQueueAsync(channel, queue, cancellationToken);   // idempotentní; vrátí i počet
            var declared = await channel.QueueDeclarePassiveAsync(RabbitMqDeadLetters.DeadQueueFor(queue), cancellationToken);
            counts[queue] = declared.MessageCount;
        }

        return counts;
    }

    /// <summary>
    /// Observabilita G2: kolik zpráv čeká v hlavní frontě každého modulu (připravené, ještě nepřevzaté consumerem).
    /// Jen pasivní dotaz – fronta se nezakládá ani nemění (existujícím frontám se argumenty nemění). Fronta, která ještě
    /// neexistuje (consumer se nespustil), ve výsledku chybí – RabbitMQ při tom zavře kanál, proto kanál na každou frontu.
    /// </summary>
    public async Task<IReadOnlyDictionary<string, uint>> GetBacklogCountsAsync(CancellationToken cancellationToken = default)
    {
        var counts = new Dictionary<string, uint>();
        foreach (var queue in Queues)
        {
            try
            {
                await using var channel = await connection.CreateChannelAsync(publisherConfirms: false, cancellationToken);
                counts[queue] = (await channel.QueueDeclarePassiveAsync(queue, cancellationToken)).MessageCount;
            }
            catch (RabbitMQ.Client.Exceptions.OperationInterruptedException)
            {
                // Fronta zatím neexistuje (404) – nic nehlásit.
            }
        }

        return counts;
    }

    /// <summary>
    /// Vrátí zprávy z <c>{fronta}.dead</c> zpět do fronty s vynulovaným počtem pokusů. Každou nejdřív publikuje
    /// (publisher confirms), teprve pak ji v dead-letter frontě potvrdí – při pádu se neztratí, nanejvýš přijde dvakrát (inbox).
    /// </summary>
    /// <returns>Kolik zpráv vrátil.</returns>
    public async Task<int> RequeueAsync(string queue, CancellationToken cancellationToken = default)
    {
        if (!Queues.Contains(queue))
        {
            throw new ArgumentException($"Fronta {queue} nepatří této službě.", nameof(queue));
        }

        await using var channel = await connection.CreateChannelAsync(publisherConfirms: true, cancellationToken);
        var moved = 0;
        while (moved < RequeueLimit
            && await channel.BasicGetAsync(RabbitMqDeadLetters.DeadQueueFor(queue), autoAck: false, cancellationToken) is { } dead)
        {
            await channel.BasicPublishAsync(string.Empty, queue, mandatory: false,
                RabbitMqDeadLetters.CopyProperties(dead.BasicProperties, attempts: 0), dead.Body, cancellationToken);
            await channel.BasicAckAsync(dead.DeliveryTag, multiple: false, cancellationToken);
            moved++;
        }

        logger.LogInformation("[Zprávy] Z {DeadQueue} vráceno {Count} zpráv do {Queue}",
            RabbitMqDeadLetters.DeadQueueFor(queue), moved, queue);
        return moved;
    }
}

/// <summary>
/// EDA krok 13: zpráva v dead-letter frontě = něco selhalo a čeká na člověka → <c>Degraded</c> (UptimeRobot upozorní).
/// Nedostupný broker hlásí jako Healthy s poznámkou – <c>/health</c> RabbitMQ záměrně nehlídá (zprávy počkají v outboxu,
/// a když čekají moc dlouho, ozve se health check outboxu).
/// </summary>
public sealed class DeadLetterHealthCheck(RabbitMqDeadLetterService deadLetters) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        IReadOnlyDictionary<string, uint> counts;
        try
        {
            counts = await deadLetters.GetCountsAsync(cancellationToken);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            return HealthCheckResult.Healthy($"RabbitMQ teď nejde zkontrolovat ({ex.Message}).");
        }

        var dead = counts.Where(c => c.Value > 0).ToDictionary(c => c.Key, c => (object)c.Value);
        return dead.Count == 0
            ? HealthCheckResult.Healthy("Žádná zpráva v dead-letter frontách.")
            : HealthCheckResult.Degraded(
                $"Zprávy v dead-letter frontách: {string.Join(", ", dead.Select(d => $"{d.Key} ({d.Value})"))}.", data: dead);
    }
}
