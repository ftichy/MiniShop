using MiniShop.Domain.Common;
using MiniShop.Domain.Exceptions;

namespace MiniShop.Domain.Shipping;

/// <summary>
/// E-shop krok 26B: zásilka u dopravce – jeden pokus o předání objednávky (jeden příkaz CreateShipmentV1). Po odmítnutí
/// může skladník zkusit znovu = nový příkaz = nová zásilka; vytvořená (<see cref="ShipmentStatus.Created"/>) je
/// k objednávce nejvýš jedna (filtrovaný unikátní index).
/// </summary>
/// <remarks>
/// Modul Shipping objednávky nezná – jen číslo objednávky z příkazu. Adresu si neukládá (zná ji dopravce i objednávka).
/// </remarks>
public class Shipment : Entity
{
    public const int CarrierMaxLength = 50;
    public const int TrackingNumberMaxLength = 50;
    public const int FailureMaxLength = 300;

    // Konstruktor pro EF Core.
    private Shipment()
    {
    }

    private Shipment(Guid commandId, int orderId, DateTime atUtc)
    {
        if (orderId <= 0)
        {
            throw new DomainException("Zásilka potřebuje číslo objednávky.");
        }

        Id = Guid.NewGuid();
        CommandId = commandId;
        OrderId = orderId;
        RequestedAtUtc = atUtc;
    }

    public Guid Id { get; private set; }

    /// <summary>ID příkazu CreateShipmentV1 = Idempotency-Key u dopravce (unikátní).</summary>
    public Guid CommandId { get; private set; }

    public int OrderId { get; private set; }

    public ShipmentStatus Status { get; private set; }

    public string? Carrier { get; private set; }

    public string? TrackingNumber { get; private set; }

    /// <summary>Proč dopravce zásilku odmítl (česky), jen u <see cref="ShipmentStatus.Failed"/>.</summary>
    public string? FailureReason { get; private set; }

    public DateTime RequestedAtUtc { get; private set; }

    /// <summary>Dopravce zásilku přijal a přidělil číslo → ShipmentCreated (→ ShipmentCreatedV1).</summary>
    public static Shipment Created(Guid commandId, int orderId, string carrier, string trackingNumber, DateTime atUtc)
    {
        if (string.IsNullOrWhiteSpace(carrier) || string.IsNullOrWhiteSpace(trackingNumber)
            || carrier.Length > CarrierMaxLength || trackingNumber.Length > TrackingNumberMaxLength)
        {
            throw new DomainException("Dopravce vrátil neplatné číslo zásilky.");
        }

        var shipment = new Shipment(commandId, orderId, atUtc)
        {
            Status = ShipmentStatus.Created,
            Carrier = carrier,
            TrackingNumber = trackingNumber
        };
        shipment.Raise(new Events.ShipmentCreated(shipment, atUtc));
        return shipment;
    }

    /// <summary>Dopravce zásilku odmítl (trvalá chyba) → ShipmentFailed (→ ShipmentFailedV1).</summary>
    public static Shipment Failed(Guid commandId, int orderId, string reason, DateTime atUtc)
    {
        var trimmed = string.IsNullOrWhiteSpace(reason) ? "Dopravce zásilku odmítl." : reason.Trim();
        var shipment = new Shipment(commandId, orderId, atUtc)
        {
            Status = ShipmentStatus.Failed,
            FailureReason = trimmed.Length > FailureMaxLength ? trimmed[..FailureMaxLength] : trimmed
        };
        shipment.Raise(new Events.ShipmentFailed(shipment, atUtc));
        return shipment;
    }

    // ---- E-shop krok 26C: průběh doručování (webhooky dopravce a dohledání) ----

    private readonly List<ShipmentTrackingEvent> _tracking = [];

    /// <summary>Historie stavů od dopravce v pořadí <see cref="ShipmentTrackingEvent.Sequence"/> (i pozdě došlé).</summary>
    public IReadOnlyCollection<ShipmentTrackingEvent> Tracking => _tracking.AsReadOnly();

    /// <summary>Nejvyšší pořadí stavu, který už zásilka má – podle něj se pozná pozdní zpráva (stav jen dopředu).</summary>
    public int LastSequence { get; private set; }

    /// <summary>Kdy přišla poslední novinka od dopravce (webhook nebo dohledání).</summary>
    public DateTime? LastCarrierUpdateAtUtc { get; private set; }

    /// <summary>Kdy se naposledy dohledávalo (GET u dopravce) – dohledání se tak neopakuje každou minutu.</summary>
    public DateTime? LastCheckedAtUtc { get; private set; }

    /// <summary>Dopravce zásilku při dohledání nezná (smazaná data, chyba) – musí rozhodnout člověk v Expedici.</summary>
    public bool UnknownAtCarrier { get; private set; }

    /// <summary>Pojistka proti souběhu: dva webhooky téže zásilky na dvou kopiích Webu – druhý zápis selže a dopravce ho zopakuje.</summary>
    public int Version { get; private set; }

    /// <summary>
    /// Stavy „u dopravce, ještě nekončí“ – jediné místo (doména, dotazy repozitáře, metriky). Nový stav doručování = sem
    /// (e-shop krok 26E: dohledání neznalo stavy z 26D, protože seznam byl i v repozitáři).
    /// </summary>
    public static readonly ShipmentStatus[] OnTheWayStatuses =
    [
        ShipmentStatus.Created, ShipmentStatus.PickedUp, ShipmentStatus.InTransit, ShipmentStatus.OutForDelivery,
        ShipmentStatus.DeliveryFailed, ShipmentStatus.ReturningToSender
    ];

    /// <summary>Konečné stavy – doručeno, nebo vráceno odesílateli (krok 26D).</summary>
    public static readonly ShipmentStatus[] FinishedStatuses = [ShipmentStatus.Delivered, ShipmentStatus.ReturnedToSender];

    /// <summary>Zásilka je u dopravce a ještě nedoručená (čeká na další stavy, případně dohledání).</summary>
    public bool IsOnTheWay => OnTheWayStatuses.Contains(Status);

    /// <summary>
    /// E-shop krok 26E: v historii chybí stav – některý webhook se ztratil (pořadí 1…<see cref="LastSequence"/> nejsou všechna).
    /// U ukončené zásilky ho jednou doplní dohledání.
    /// </summary>
    public bool HasHistoryGaps => _tracking.Count < LastSequence;

    /// <summary>
    /// Nový stav od dopravce. <paramref name="sequence"/> = pořadí u zásilky (přirozený klíč idempotence):
    /// stejné podruhé = duplicita (nic), nižší než aktuální = jen do historie (stav jen dopředu), vyšší = nový stav;
    /// doručeno → <see cref="Events.ShipmentDelivered"/>, vráceno odesílateli (krok 26D) → <see cref="Events.ShipmentReturned"/>.
    /// Vrací, jestli se něco změnilo.
    /// </summary>
    /// <remarks>
    /// Konečný stav (doručeno / vráceno) už nic nepřepíše – kdyby dopravce poslal vyšší pořadí po konci, jde jen do historie.
    /// </remarks>
    public bool ApplyTracking(int sequence, ShipmentStatus status, DateTime occurredAtUtc, DateTime receivedAtUtc)
    {
        if (status is ShipmentStatus.Created or ShipmentStatus.Failed)
        {
            throw new DomainException("Od dopravce může přijít jen stav doručování.");
        }

        if (Status == ShipmentStatus.Failed || sequence <= 0 || _tracking.Any(t => t.Sequence == sequence))
        {
            return false;   // odmítnutá zásilka, nesmysl, nebo duplicita
        }

        _tracking.Add(new ShipmentTrackingEvent(sequence, status, occurredAtUtc, receivedAtUtc));
        LastCarrierUpdateAtUtc = receivedAtUtc;
        UnknownAtCarrier = false;
        Version++;

        if (sequence > LastSequence && !IsFinished)
        {
            LastSequence = sequence;
            var previous = Status;
            Status = status;
            if (status != previous)
            {
                // Vzhled krok F3b: signál otevřeným stránkám (detail objednávky, Expedice) – jen skutečná změna stavu,
                // ne duplicita ani pozdní zpráva (ty sem nedojdou – pořadí nebo konečný stav).
                Raise(new Events.ShipmentStatusChanged(this, status, occurredAtUtc));
            }

            if (status == ShipmentStatus.Delivered)
            {
                Raise(new Events.ShipmentDelivered(this, occurredAtUtc));
            }
            else if (status == ShipmentStatus.ReturnedToSender)
            {
                Raise(new Events.ShipmentReturned(this, occurredAtUtc));
            }
        }

        return true;
    }

    /// <summary>Konečný stav – doručeno, nebo vráceno odesílateli (krok 26D).</summary>
    public bool IsFinished => FinishedStatuses.Contains(Status);

    /// <summary>
    /// Dohledání proběhlo (s výsledkem nebo bez) – další až po čase. Ukončenou zásilku, kterou dopravce už nezná (smazal
    /// data), neoznačí jako neznámou – je doručená/vrácená a člověk nemá co řešit (krok 26E).
    /// </summary>
    public void MarkChecked(DateTime checkedAtUtc, bool unknownAtCarrier)
    {
        LastCheckedAtUtc = checkedAtUtc;
        UnknownAtCarrier = unknownAtCarrier && !IsFinished;
        Version++;
    }
}

/// <summary>
/// E-shop krok 26B/26C: stav zásilky v našem modulu (ne slovník dopravce – ten překládá CarrierApiClient).
/// Nové hodnoty jen na konec (ukládá se text).
/// </summary>
public enum ShipmentStatus
{
    Created,
    Failed,
    PickedUp,
    InTransit,
    OutForDelivery,
    Delivered,

    /// <summary>Krok 26D: kurýr příjemce nezastihl (zkusí znovu, nebo vrátí).</summary>
    DeliveryFailed,

    /// <summary>Krok 26D: zásilka jede zpět k nám.</summary>
    ReturningToSender,

    /// <summary>Krok 26D: zásilka je zpět u nás – konečný stav.</summary>
    ReturnedToSender
}

/// <summary>E-shop krok 26C: jeden stav od dopravce (řádek historie). Unikátní zásilka + <see cref="Sequence"/>.</summary>
public sealed class ShipmentTrackingEvent
{
    // Konstruktor pro EF Core.
    private ShipmentTrackingEvent()
    {
    }

    public ShipmentTrackingEvent(int sequence, ShipmentStatus status, DateTime occurredAtUtc, DateTime receivedAtUtc)
    {
        Sequence = sequence;
        Status = status;
        OccurredAtUtc = occurredAtUtc;
        ReceivedAtUtc = receivedAtUtc;
    }

    public int Id { get; private set; }

    public Guid ShipmentId { get; private set; }

    public int Sequence { get; private set; }

    public ShipmentStatus Status { get; private set; }

    /// <summary>Kdy se to u dopravce stalo.</summary>
    public DateTime OccurredAtUtc { get; private set; }

    /// <summary>Kdy jsme se to dozvěděli (rozdíl = zpoždění webhooku).</summary>
    public DateTime ReceivedAtUtc { get; private set; }
}
