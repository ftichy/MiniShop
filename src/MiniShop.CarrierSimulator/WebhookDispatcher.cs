using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace MiniShop.CarrierSimulator;

/// <summary>
/// „Život“ zásilek: posouvá je po časové ose (<see cref="CarrierStatuses.Timeline"/>) a posílá webhooky s podpisem.
/// Volá ho <see cref="CarrierWorker"/> každý tick; testy přímo (<see cref="ProcessAsync"/>) s posunutým časem.
/// </summary>
public sealed class WebhookDispatcher(
    ShipmentStore store, ChaosDice dice, IOptions<CarrierOptions> options, TimeProvider time,
    IHttpClientFactory httpClients, ILogger<WebhookDispatcher> log)
{
    public const string HttpClientName = "webhooks";
    public const string SignatureHeader = "Carrier-Signature";
    public const string EventType = "shipment.status_changed";

    private static readonly JsonSerializerOptions PayloadJson = new(JsonSerializerDefaults.Web);

    public async Task ProcessAsync(CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow().UtcDateTime;
        AdvanceShipments(now);
        await SendDueWebhooksAsync(now, cancellationToken);
    }

    /// <summary>
    /// Stav, jehož čas nastal, se zapíše jako událost s PLÁNOVANÝM časem (když simulátor chvíli neběžel, dožene víc stavů
    /// naráz se správnými časy). Webhook vznikne jen s nastavenou adresou; zlobení rozhodne, jestli dvakrát, později, nebo vůbec.
    /// </summary>
    private void AdvanceShipments(DateTime now)
    {
        var settings = options.Value;
        var step = TimeSpan.FromSeconds(settings.StepSeconds);

        store.Update(state =>
        {
            foreach (var shipment in state.Shipments)
            {
                while (shipment.Events.Count < CarrierStatuses.Timeline.Length)
                {
                    var index = shipment.Events.Count;
                    var occurredAt = shipment.CreatedAtUtc + step * (index + 1);
                    if (occurredAt > now)
                    {
                        break;
                    }

                    var evt = new ShipmentEvent
                    {
                        Id = "evt_" + Guid.NewGuid().ToString("N"),
                        Sequence = index + 1,
                        Status = CarrierStatuses.Timeline[index],
                        OccurredAtUtc = occurredAt
                    };
                    shipment.Events.Add(evt);
                    log.LogInformation("Zásilka {TrackingNumber}: {Status}", shipment.TrackingNumber, evt.Status);
                    PlanWebhook(state, shipment, evt, step, settings);
                }
            }

            // Úklid: doručené zásilky (s adresou příjemce) jen po dobu RetentionDays.
            var removed = state.Shipments.RemoveAll(s =>
                s.DeliveredAtUtc is { } delivered && delivered < now.AddDays(-settings.RetentionDays));
            if (removed > 0)
            {
                log.LogInformation("Úklid: smazáno {Count} doručených zásilek", removed);
            }

            return 0;
        });
    }

    private void PlanWebhook(CarrierState state, Shipment shipment, ShipmentEvent evt, TimeSpan step, CarrierOptions settings)
    {
        if (string.IsNullOrWhiteSpace(settings.WebhookUrl))
        {
            return;
        }

        var chaos = settings.Chaos;
        if (dice.Roll(chaos.DropRate))
        {
            log.LogInformation("Zlobení: webhook {EventId} se nepošle", evt.Id);
            return;
        }

        var payload = JsonSerializer.Serialize(new
        {
            id = evt.Id,
            type = EventType,
            created = evt.OccurredAtUtc,
            data = new
            {
                trackingNumber = shipment.TrackingNumber,
                reference = shipment.Reference,
                status = evt.Status,
                sequence = evt.Sequence,
                occurredAt = evt.OccurredAtUtc
            }
        }, PayloadJson);

        // Zlobení „mimo pořadí“: webhook počká až za ten následující (poslední stav nemá na co čekat → předběhne předchozí).
        var dueAt = evt.OccurredAtUtc;
        if (evt.Sequence < CarrierStatuses.Timeline.Length && dice.Roll(chaos.ReorderRate))
        {
            dueAt = evt.OccurredAtUtc + step + TimeSpan.FromSeconds(1);
            log.LogInformation("Zlobení: webhook {EventId} se zdrží za následující", evt.Id);
        }

        state.Deliveries.Add(new WebhookDelivery { EventId = evt.Id, TrackingNumber = shipment.TrackingNumber, Payload = payload, DueAtUtc = dueAt });

        if (dice.Roll(chaos.DuplicateRate))
        {
            var half = TimeSpan.FromTicks(Math.Max(step.Ticks / 2, TimeSpan.TicksPerSecond));
            state.Deliveries.Add(new WebhookDelivery { EventId = evt.Id, TrackingNumber = shipment.TrackingNumber, Payload = payload, DueAtUtc = dueAt + half });
            log.LogInformation("Zlobení: webhook {EventId} přijde dvakrát", evt.Id);
        }
    }

    private async Task SendDueWebhooksAsync(DateTime now, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        if (string.IsNullOrWhiteSpace(settings.WebhookUrl))
        {
            return;
        }

        var due = store.Read(state => state.Deliveries
            .Where(d => d.DueAtUtc <= now)
            .OrderBy(d => d.DueAtUtc)
            .Select(d => (d.EventId, d.TrackingNumber, d.Payload, d.DueAtUtc))
            .ToList());

        var client = httpClients.CreateClient(HttpClientName);
        foreach (var delivery in due)
        {
            var ok = await TrySendAsync(client, settings, delivery.Payload, delivery.EventId, cancellationToken);
            store.Update(state =>
            {
                var item = state.Deliveries.FirstOrDefault(d => d.EventId == delivery.EventId && d.DueAtUtc == delivery.DueAtUtc);
                if (item is null)
                {
                    return 0;
                }

                if (ok)
                {
                    state.Deliveries.Remove(item);
                    return 0;
                }

                item.FailedAttempts++;
                if (item.FailedAttempts >= settings.MaxWebhookAttempts)
                {
                    state.Deliveries.Remove(item);
                    log.LogWarning("Webhook {EventId} (zásilka {TrackingNumber}) vzdán po {Attempts} pokusech – stav už jen přes GET",
                        item.EventId, item.TrackingNumber, item.FailedAttempts);
                    return 0;
                }

                item.DueAtUtc = now + RetryDelay(item.FailedAttempts);
                return 0;
            });
        }
    }

    private async Task<bool> TrySendAsync(HttpClient client, CarrierOptions settings, string payload, string eventId, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, settings.WebhookUrl)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            };
            request.Headers.TryAddWithoutValidation(SignatureHeader, Sign(settings.WebhookSecret, payload, time.GetUtcNow()));
            using var response = await client.SendAsync(request, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                return true;
            }

            log.LogWarning("Webhook {EventId}: e-shop odpověděl {StatusCode}", eventId, (int)response.StatusCode);
            return false;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            log.LogWarning("Webhook {EventId}: e-shop nedostupný ({Error})", eventId, ex.GetType().Name);
            return false;
        }
    }

    /// <summary>Další pokus: 10 s, 20 s, 40 s … nejvýš 10 minut.</summary>
    public static TimeSpan RetryDelay(int failedAttempts) =>
        TimeSpan.FromSeconds(Math.Min(10 * Math.Pow(2, failedAttempts - 1), 600));

    /// <summary>
    /// Podpis jako u Stripu: <c>t=&lt;unix čas&gt;,v1=&lt;hex HMAC-SHA256(tajemství, "t.tělo")&gt;</c>. Čas v podpisu brání
    /// přehrání staré zprávy (e-shop odmítne starší než pár minut), HMAC tomu, aby zprávu podvrhl někdo bez tajemství.
    /// </summary>
    public static string Sign(string secret, string payload, DateTimeOffset at)
    {
        var timestamp = at.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        var hash = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes($"{timestamp}.{payload}"));
        return $"t={timestamp},v1={Convert.ToHexStringLower(hash)}";
    }
}

/// <summary>Plánovač: každý tick posune zásilky a pošle webhooky. Simulátor běží v jedné kopii (žádná volba vedoucí).</summary>
public sealed class CarrierWorker(IServiceProvider services, IOptions<CarrierOptions> options, ILogger<CarrierWorker> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(options.Value.TickMilliseconds));
        var dispatcher = services.GetRequiredService<WebhookDispatcher>();
        do
        {
            try
            {
                await dispatcher.ProcessAsync(stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                log.LogError(ex, "Plánovač dopravce selhal – zkusí to v dalším kroku");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
