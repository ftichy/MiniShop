using System.Text;
using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MiniShop.Application.Exceptions;
using MiniShop.Application.Reporting;
using MiniShop.Contracts.Orders;
using MiniShop.Contracts.Shipping;
using MiniShop.Domain.Reporting;
using MiniShop.Infrastructure.Integration.RabbitMq;
using MiniShop.Infrastructure.Outbox;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace MiniShop.Infrastructure.Reporting;

/// <summary>
/// Proud B2: <b>trvalý čtenář proudu</b> <c>minishop.events</c> – staví přehled Rychlost vyřízení jen přehráváním zpráv.
/// Na rozdíl od stránky Proud zpráv (přečte a odpojí se) poslouchá pořád a pamatuje si <b>záložku</b> (offset) v databázi.
/// </summary>
/// <remarks>
/// <para>
/// Zprávy sbírá do dávek (nejvýš <see cref="BatchSize"/> nebo <see cref="BatchWindow"/>) a každou dávku uloží
/// <b>v jedné transakci</b>: změny časů objednávek + novou záložku. Po pádu pokračuje od uložené záložky – žádná zpráva
/// se nezapočítá dvakrát ani nevynechá (efekt „přesně jednou“ bez inboxu).
/// </para>
/// <para>
/// Přestavění (<see cref="LeadTimeReportService.RebuildAsync"/>) zvýší generaci záložky. Čtenář ji kontroluje u každé
/// dávky: jiná generace (nebo souběh – concurrency token) = zahodit rozpracované a číst znovu od začátku proudu.
/// Běží jen na vedoucí kopii Reporting.Api. Výpadek RabbitMQ nebo databáze = za chvíli znovu od záložky.
/// </para>
/// </remarks>
public sealed class LeadTimeProjectionWorker(
    RabbitMqConnection connection,
    IServiceScopeFactory scopes,
    TimeProvider timeProvider,
    ILogger<LeadTimeProjectionWorker> logger) : BackgroundService
{
    public const int BatchSize = 100;

    public static readonly TimeSpan BatchWindow = TimeSpan.FromSeconds(1);

    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(5);

    /// <summary>Jediné zprávy, které projekce čte (ostatní v proudu jen přeskočí – záložka se posune i přes ně).</summary>
    private static readonly HashSet<string> Relevant = [nameof(OrderStatusChangedV1), nameof(ShipmentStatusChangedV1)];

    private sealed record StreamMessage(long Offset, string Type, string Payload, DateTime TimestampUtc);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield();
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ReadUntilRestartAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogWarning("Projekce Rychlost vyřízení: čtení proudu přerušeno ({Error}), za {Delay} s znovu od záložky.",
                    ex.Message, RetryDelay.TotalSeconds);
                try
                {
                    await Task.Delay(RetryDelay, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }
    }

    /// <summary>Čte od záložky, dokud nepřijde přestavění (jiná generace) – pak se vrátí a čte se znovu od začátku.</summary>
    private async Task ReadUntilRestartAsync(CancellationToken stoppingToken)
    {
        var (offset, generation) = await ReadCheckpointAsync(stoppingToken);

        await using var channel = await connection.CreateChannelAsync(publisherConfirms: false, stoppingToken);
        await channel.BasicQosAsync(prefetchSize: 0, prefetchCount: 500, global: false, stoppingToken);   // proud ho vyžaduje

        var inbox = Channel.CreateUnbounded<StreamMessage>(new UnboundedChannelOptions { SingleReader = true });
        channel.ChannelShutdownAsync += (_, args) =>
        {
            inbox.Writer.TryComplete(new InvalidOperationException($"Spojení s RabbitMQ se přerušilo: {args.ReplyText}"));
            return Task.CompletedTask;
        };

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += async (_, delivery) =>
        {
            var properties = delivery.BasicProperties;
            inbox.Writer.TryWrite(new StreamMessage(
                RabbitMqEventStream.OffsetOf(properties),
                properties.Type ?? string.Empty,
                Encoding.UTF8.GetString(delivery.Body.Span),
                DateTimeOffset.FromUnixTimeSeconds(properties.Timestamp.UnixTime).UtcDateTime));
            await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false, stoppingToken);   // u proudu jen řízení toku
        };

        // Záložka: další zpráva po uložené, nebo od začátku retence (po přestavění, při prvním spuštění).
        object start = offset is { } last ? last + 1 : "first";
        await channel.BasicConsumeAsync(RabbitMqEventStream.StreamName, autoAck: false, consumerTag: string.Empty,
            noLocal: false, exclusive: false, arguments: new Dictionary<string, object?> { ["x-stream-offset"] = start },
            consumer, stoppingToken);
        logger.LogInformation("Projekce Rychlost vyřízení čte proud od {Start} (generace {Generation})", start, generation);

        var batch = new List<StreamMessage>();
        while (true)
        {
            using (var window = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken))
            {
                window.CancelAfter(BatchWindow);
                try
                {
                    while (batch.Count < BatchSize)
                    {
                        var message = await inbox.Reader.ReadAsync(window.Token);
                        if (offset is { } done && message.Offset <= done)
                        {
                            continue;   // už zpracovaná (broker může začít na začátku bloku)
                        }

                        batch.Add(message);
                    }
                }
                catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
                {
                    // okno dávky uplynulo – uložit, co je
                }
            }

            if (!await CommitAsync(batch, generation, stoppingToken))
            {
                logger.LogInformation("Projekce Rychlost vyřízení: přestavění – čte se znovu od začátku proudu");
                return;
            }

            if (batch.Count > 0)
            {
                offset = batch[^1].Offset;
                batch.Clear();
            }
        }
    }

    private async Task<(long? Offset, int Generation)> ReadCheckpointAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var checkpoint = await scope.ServiceProvider.GetRequiredService<ILeadTimeRepository>()
            .GetCheckpointAsync(LeadTimeReportService.CheckpointName, cancellationToken);
        return (checkpoint.Offset, checkpoint.Generation);
    }

    /// <summary>
    /// Uloží dávku a záložku v jedné transakci. False = mezitím přestavění (jiná generace nebo souběh) – dávka se zahodí.
    /// Prázdná dávka jen zkontroluje generaci (přestavění, zatímco proud mlčí).
    /// </summary>
    private async Task<bool> CommitAsync(List<StreamMessage> batch, int generation, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<ILeadTimeRepository>();
        var checkpoint = await repository.GetCheckpointAsync(LeadTimeReportService.CheckpointName, cancellationToken);
        if (checkpoint.Generation != generation)
        {
            return false;
        }

        if (batch.Count == 0)
        {
            return true;
        }

        var messages = batch
            .Where(m => Relevant.Contains(m.Type))
            .Select(m => TryRead(m))
            .OfType<MiniShop.Contracts.IIntegrationMessage>()
            .ToList();
        var orderIds = messages.Select(LeadTimeProjection.OrderIdOf).OfType<int>().Distinct().ToList();
        var leadTimes = new Dictionary<int, OrderLeadTime>(await repository.GetByOrderIdsAsync(orderIds, cancellationToken));
        foreach (var message in messages)
        {
            var orderId = LeadTimeProjection.OrderIdOf(message)!.Value;
            if (!leadTimes.TryGetValue(orderId, out var leadTime))
            {
                leadTime = OrderLeadTime.Create(orderId);
                leadTimes[orderId] = leadTime;
                repository.Add(leadTime);
            }

            LeadTimeProjection.Apply(leadTime, message);
        }

        checkpoint.Advance(batch[^1].Offset, batch[^1].TimestampUtc, timeProvider.GetUtcNow().UtcDateTime);
        try
        {
            await scope.ServiceProvider.GetRequiredService<IReportingUnitOfWork>().SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (ConcurrencyConflictException)
        {
            return false;   // souběh s přestavěním – nic se neuložilo
        }
    }

    private MiniShop.Contracts.IIntegrationMessage? TryRead(StreamMessage message)
    {
        try
        {
            return IntegrationMessageReader.Read(message.Type, message.Payload);
        }
        catch (Exception ex)
        {
            // Proud nemá dead-letter frontu: nečitelnou zprávu jen zalogovat a jít dál (záložka se posune).
            logger.LogWarning("Projekce Rychlost vyřízení přeskočila nečitelnou zprávu {Offset} ({Type}): {Error}",
                message.Offset, message.Type, ex.Message);
            return null;
        }
    }
}
