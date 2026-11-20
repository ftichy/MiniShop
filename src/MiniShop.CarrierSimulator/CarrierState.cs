using System.Text.Json.Serialization;

namespace MiniShop.CarrierSimulator;

/// <summary>Stav dopravce v souboru – zásilky a webhooky čekající na odeslání (přežijí restart kontejneru).</summary>
public sealed class CarrierState
{
    public List<Shipment> Shipments { get; set; } = [];

    public List<WebhookDelivery> Deliveries { get; set; } = [];
}

/// <summary>
/// Zásilka u dopravce. Stavy jsou slovník DOPRAVCE (<see cref="CarrierStatuses"/>), ne MiniShopu – e-shop si je přeloží
/// (anti-corruption layer, krok 26C). Adresa příjemce je osobní údaj: nikdy do logu, smaže se po <c>RetentionDays</c>.
/// </summary>
public sealed class Shipment
{
    public string TrackingNumber { get; set; } = "";

    /// <summary>Číslo, pod kterým zásilku zná e-shop (u MiniShopu číslo objednávky).</summary>
    public string Reference { get; set; } = "";

    public Recipient Recipient { get; set; } = new();

    public int? WeightGrams { get; set; }

    public decimal? CashOnDelivery { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public string IdempotencyKey { get; set; } = "";

    /// <summary>Otisk těla požadavku – stejný klíč s jiným obsahem = chyba e-shopu (409).</summary>
    public string RequestHash { get; set; } = "";

    /// <summary>
    /// Krok 26D: zásilka „nepůjde doručit“ – rozhodne se kostkou už při vzniku (<c>Chaos:UndeliverableRate</c>), takže
    /// časová osa je od začátku daná a po restartu stejná. Jde cestou <see cref="CarrierStatuses.UndeliverableTimeline"/>.
    /// </summary>
    public bool Undeliverable { get; set; }

    public List<ShipmentEvent> Events { get; set; } = [];

    [JsonIgnore]
    public string Status => Events.Count == 0 ? CarrierStatuses.Created : Events[^1].Status;

    /// <summary>Stavy, kterými tahle zásilka projde (podle <see cref="Undeliverable"/>).</summary>
    [JsonIgnore]
    public string[] Timeline => Undeliverable ? CarrierStatuses.UndeliverableTimeline : CarrierStatuses.Timeline;

    /// <summary>Kdy zásilka skončila (doručena, nebo vrácena odesílateli) – od té doby běží lhůta úklidu.</summary>
    [JsonIgnore]
    public DateTime? FinishedAtUtc => Events.LastOrDefault(e => CarrierStatuses.IsFinal(e.Status))?.OccurredAtUtc;
}

public sealed class Recipient
{
    public string Name { get; set; } = "";

    public string Street { get; set; } = "";

    public string City { get; set; } = "";

    public string PostalCode { get; set; } = "";
}

/// <summary>Jedna změna stavu. <see cref="Sequence"/> = pořadí u zásilky (1, 2, …) – podle něj pozná e-shop starší zprávu.</summary>
public sealed class ShipmentEvent
{
    public string Id { get; set; } = "";

    public int Sequence { get; set; }

    public string Status { get; set; } = "";

    public DateTime OccurredAtUtc { get; set; }
}

/// <summary>Webhook čekající na odeslání (nebo na další pokus).</summary>
public sealed class WebhookDelivery
{
    public string EventId { get; set; } = "";

    public string TrackingNumber { get; set; } = "";

    /// <summary>Hotové tělo JSON – opakování i duplicita posílají přesně stejné bajty (podpis sedí).</summary>
    public string Payload { get; set; } = "";

    public DateTime DueAtUtc { get; set; }

    /// <summary>Kolik pokusů už selhalo.</summary>
    public int FailedAttempts { get; set; }
}

/// <summary>Stavy zásilky ve slovníku dopravce – pořadí v <see cref="Timeline"/> je pořadí, ve kterém nastávají.</summary>
public static class CarrierStatuses
{
    public const string Created = "created";
    public const string Accepted = "accepted";
    public const string InTransit = "in_transit";
    public const string OutForDelivery = "out_for_delivery";
    public const string Delivered = "delivered";

    /// <summary>Krok 26D: kurýr příjemce nezastihl.</summary>
    public const string DeliveryFailed = "delivery_failed";

    /// <summary>Krok 26D: zásilka jede zpět k odesílateli.</summary>
    public const string Returning = "returning";

    /// <summary>Krok 26D: zásilka je zpět u odesílatele (e-shopu) – konečný stav jako <see cref="Delivered"/>.</summary>
    public const string ReturnedToSender = "returned_to_sender";

    /// <summary>Stavy po vytvoření; k-tý nastane k × <c>StepSeconds</c> po vytvoření zásilky.</summary>
    public static readonly string[] Timeline = [Accepted, InTransit, OutForDelivery, Delivered];

    /// <summary>Krok 26D: dvakrát nezastižen, pak zpět odesílateli.</summary>
    public static readonly string[] UndeliverableTimeline =
        [Accepted, InTransit, OutForDelivery, DeliveryFailed, OutForDelivery, DeliveryFailed, Returning, ReturnedToSender];

    public static bool IsFinal(string status) => status is Delivered or ReturnedToSender;
}
