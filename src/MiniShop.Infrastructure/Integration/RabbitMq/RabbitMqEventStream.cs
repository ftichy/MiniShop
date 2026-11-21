using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using MiniShop.Infrastructure.Outbox;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using RabbitMQ.Client.Exceptions;

namespace MiniShop.Infrastructure.Integration.RabbitMq;

/// <summary>
/// Proud B1: <b>proud zpráv</b> <c>minishop.events</c> – fronta typu <c>stream</c> (log) navázaná na exchange VŠECH kontraktů.
/// Na rozdíl od běžné fronty se zprávy čtením nemažou: každý čtenář si určí, odkud čte (záložka = offset nebo čas),
/// a nejstarší zprávy maže až retence (<see cref="MaxAge"/>, <see cref="MaxLengthBytes"/>).
/// </summary>
/// <remarks>
/// Přirovnání: fronta = schránka na dopisy (vyzvednuto = pryč), proud = kniha jízd (zápisy zůstávají, každý čtenář má
/// svou záložku). Proud není zdroj pravdy – pravda je v databázích modulů; tohle je kopie pro čtení a přehrávání.
/// </remarks>
public static class RabbitMqEventStream
{
    public const string StreamName = "minishop.events";

    /// <summary>Retence podle stáří (formát RabbitMQ: <c>14D</c>).</summary>
    public const string MaxAge = "14D";

    /// <summary>Retence podle velikosti – nejvýš 1 GB na disku VM.</summary>
    public const long MaxLengthBytes = 1_000_000_000;

    /// <summary>Segmenty po 50 MB – retence maže celé segmenty, menší = jemnější mazání (výchozí 500 MB).</summary>
    public const long SegmentBytes = 50_000_000;

    /// <summary>Pořadí zprávy v proudu (offset) – broker ho přidá do hlavičky <c>x-stream-offset</c> každé doručené zprávy.</summary>
    public static long OffsetOf(IReadOnlyBasicProperties properties) =>
        properties.Headers?.TryGetValue("x-stream-offset", out var value) == true && value is long number ? number : -1;

    /// <summary>Založí proud (idempotentně – stejné argumenty podruhé nic nezmění) a naváže ho na exchange všech kontraktů.</summary>
    public static async Task SetupAsync(RabbitMqConnection connection, CancellationToken cancellationToken)
    {
        await using var channel = await connection.CreateChannelAsync(publisherConfirms: false, cancellationToken);
        await channel.QueueDeclareAsync(StreamName, durable: true, exclusive: false, autoDelete: false,
            arguments: new Dictionary<string, object?>
            {
                ["x-queue-type"] = "stream",
                ["x-max-age"] = MaxAge,
                ["x-max-length-bytes"] = MaxLengthBytes,
                ["x-stream-max-segment-size-bytes"] = SegmentBytes
            },
            cancellationToken: cancellationToken);

        foreach (var contract in IntegrationMessageReader.ContractNames)
        {
            await RabbitMqTopology.DeclareExchangeAsync(channel, contract, cancellationToken);
            await channel.QueueBindAsync(StreamName, contract, routingKey: string.Empty, cancellationToken: cancellationToken);
        }
    }
}

/// <summary>Proud B1: jedna zpráva z proudu – pořadí (offset), čas, typ, číslo objednávky a obsah se zamaskovanými osobními údaji.</summary>
public sealed record EventStreamEntry(long Offset, DateTime OccurredAtUtc, string Type, string? MessageId, int? OrderId, string Payload);

/// <summary>Co číst: od kdy, volitelně jen jeden typ zprávy nebo jedna objednávka.</summary>
public sealed record EventStreamQuery(DateTime FromUtc, string? Type = null, int? OrderId = null, int Limit = 2000);

/// <param name="Truncated">Zpráv bylo víc než limit – zobrazené jsou nejnovější.</param>
public sealed record EventStreamResult(IReadOnlyList<EventStreamEntry> Entries, bool Truncated, int Scanned);

/// <summary>Proud nejde přečíst (RabbitMQ neběží, proud ještě nevznikl).</summary>
public sealed class EventStreamUnavailableException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>Proud B1: čtení proudu zpráv (stránka Proud zpráv). V testech Webu podvržená.</summary>
public interface IEventStreamReader
{
    Task<EventStreamResult> ReadAsync(EventStreamQuery query, CancellationToken cancellationToken = default);
}

/// <summary>
/// Proud B1: přečte proud <b>od času</b> (záložka <c>x-stream-offset</c> = časové razítko) až do „teď“ a odpojí se.
/// Proud se tím nijak nemění – potvrzení (ack) u proudu jen řídí, kolik zpráv broker pošle dál, nic nemaže.
/// </summary>
/// <remarks>
/// „Teď“ = chvíli nepřišla žádná zpráva (<see cref="IdleTimeout"/>). AMQP neumí říct „kde proud končí“ – nativní protokol
/// streamů ano, ale potřebuje plugin a port navíc (rozhodnutí v příručce <c>eda/proud-zprav.md</c>).
/// Časová záložka začíná na začátku bloku (chunk), kde čas leží – starší zprávy z bloku se odfiltrují podle času zprávy.
/// </remarks>
public sealed class RabbitMqEventStreamReader(RabbitMqConnection connection, ILogger<RabbitMqEventStreamReader> logger)
    : IEventStreamReader
{
    /// <summary>Konec čtení: tak dlouho bez nové zprávy = jsme na konci proudu.</summary>
    public static readonly TimeSpan IdleTimeout = TimeSpan.FromMilliseconds(800);

    /// <summary>Pojistka – víc zpráv se nepřečte ani při velmi širokém „od kdy“.</summary>
    public const int MaxScanned = 50_000;

    public async Task<EventStreamResult> ReadAsync(EventStreamQuery query, CancellationToken cancellationToken = default)
    {
        IChannel channel;
        try
        {
            channel = await connection.CreateChannelAsync(publisherConfirms: false, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new EventStreamUnavailableException("RabbitMQ není dostupný.", ex);
        }

        await using var _ = channel;
        var matched = new List<EventStreamEntry>();
        var scanned = 0;
        var lastActivity = DateTime.UtcNow;
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var sync = new Lock();

        try
        {
            await channel.BasicQosAsync(prefetchSize: 0, prefetchCount: 500, global: false, cancellationToken);   // proud ho vyžaduje
            var consumer = new AsyncEventingBasicConsumer(channel);
            consumer.ReceivedAsync += async (_, delivery) =>
            {
                var entry = ToEntry(delivery);
                lock (sync)
                {
                    lastActivity = DateTime.UtcNow;
                    scanned++;
                    if (entry.OccurredAtUtc >= query.FromUtc && Matches(entry, query))
                    {
                        matched.Add(entry);
                    }

                    if (scanned >= MaxScanned)
                    {
                        done.TrySetResult();
                    }
                }

                await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false, cancellationToken);
            };

            var from = new DateTimeOffset(DateTime.SpecifyKind(query.FromUtc, DateTimeKind.Utc)).ToUnixTimeSeconds();
            var tag = await channel.BasicConsumeAsync(RabbitMqEventStream.StreamName, autoAck: false, consumerTag: string.Empty,
                noLocal: false, exclusive: false,
                arguments: new Dictionary<string, object?> { ["x-stream-offset"] = new AmqpTimestamp(from) },
                consumer, cancellationToken);

            while (!done.Task.IsCompleted)
            {
                await Task.Delay(100, cancellationToken);
                lock (sync)
                {
                    if (DateTime.UtcNow - lastActivity >= IdleTimeout)
                    {
                        break;
                    }
                }
            }

            await channel.BasicCancelAsync(tag, noWait: false, cancellationToken);
        }
        catch (OperationInterruptedException ex)
        {
            logger.LogWarning("Proud {Stream} nejde přečíst: {Error}", RabbitMqEventStream.StreamName, ex.Message);
            throw new EventStreamUnavailableException("Proud zpráv ještě nevznikl nebo ho RabbitMQ odmítl přečíst.", ex);
        }

        lock (sync)
        {
            var ordered = matched.OrderBy(e => e.Offset).ToList();
            var truncated = ordered.Count > query.Limit;
            return new EventStreamResult(truncated ? ordered[^query.Limit..] : ordered, truncated, scanned);
        }
    }

    private static bool Matches(EventStreamEntry entry, EventStreamQuery query) =>
        (string.IsNullOrEmpty(query.Type) || entry.Type == query.Type)
        && (query.OrderId is null || entry.OrderId == query.OrderId);

    private static EventStreamEntry ToEntry(BasicDeliverEventArgs delivery)
    {
        var properties = delivery.BasicProperties;
        var offset = RabbitMqEventStream.OffsetOf(properties);
        var payload = Encoding.UTF8.GetString(delivery.Body.Span);
        DateTime occurredAt = DateTimeOffset.FromUnixTimeSeconds(properties.Timestamp.UnixTime).UtcDateTime;
        int? orderId = null;
        try
        {
            using var json = JsonDocument.Parse(payload);
            if (json.RootElement.TryGetProperty("OccurredAtUtc", out var at) && at.TryGetDateTime(out var parsed))
            {
                occurredAt = DateTime.SpecifyKind(parsed, DateTimeKind.Utc);   // přesný čas faktu (razítko AMQP má jen sekundy)
            }

            if (json.RootElement.TryGetProperty("OrderId", out var id) && id.TryGetInt32(out var parsedId))
            {
                orderId = parsedId;
            }
        }
        catch (JsonException)
        {
            // nečitelný obsah – ukáže se, jak je (zamaskovat nejde, proto ho nahradíme)
            payload = "(nečitelný JSON)";
        }

        return new EventStreamEntry(offset, occurredAt, properties.Type ?? "?", properties.MessageId, orderId,
            PersonalDataMask.Mask(payload));
    }
}
