using MiniShop.Domain.Exceptions;

namespace MiniShop.Domain.Orders;

/// <summary>
/// E-shop krok 28B (směr C): balík objednávky – část zboží, kterou zabalí a odešle jeden sklad. Rozdělení rozhoduje sklad
/// (přidělení zásob) a Ordering se ho dozví zprávou <c>StockReservedV2</c>. Patří do agregátu <see cref="Order"/>:
/// pravidla „odesláno / doručeno až po všech balících“ (krok 28C) bude hlídat objednávka.
/// </summary>
/// <remarks>
/// Ordering sklady nezná – kód a název skladu jsou jen text ze zprávy (jako název produktu v položce).
/// Objednávka bez balíků (z doby před krokem 28B) = jeden balík z výchozího skladu (<see cref="Order.EffectiveParcels"/>).
/// </remarks>
public sealed class OrderParcel
{
    /// <summary>Výchozí sklad – do kroku 28A byly všechny zásoby v Praze.</summary>
    public const string DefaultWarehouseCode = "PRG";

    public const string DefaultWarehouseName = "Praha";

    private readonly List<OrderParcelLine> _lines = new();

    // Konstruktor pro EF Core.
    private OrderParcel()
    {
    }

    internal OrderParcel(int number, string warehouseCode, string warehouseName, IEnumerable<OrderParcelLine> lines)
    {
        Number = number;
        WarehouseCode = warehouseCode;
        WarehouseName = warehouseName;
        _lines.AddRange(lines);
        if (_lines.Count == 0 || _lines.Any(l => l.Quantity <= 0))
        {
            throw new DomainException("Balík musí obsahovat aspoň jednu položku s kladným počtem kusů.");
        }
    }

    public int Id { get; private set; }

    public int OrderId { get; private set; }

    /// <summary>Číslo balíku v objednávce (1, 2, …) – od kroku 28C ho ponese i zásilka u dopravce.</summary>
    public int Number { get; private set; }

    public string WarehouseCode { get; private set; } = string.Empty;

    public string WarehouseName { get; private set; } = string.Empty;

    public IReadOnlyCollection<OrderParcelLine> Lines => _lines.AsReadOnly();

    // ---- E-shop krok 28C: vlastní zásilka balíku u dopravce ----

    /// <summary>Kdy skladník balík předal dopravci (čeká se na číslo zásilky); null = nepředán nebo odmítnut.</summary>
    public DateTime? ShipmentRequestedAtUtc { get; private set; }

    public string? Carrier { get; private set; }

    public string? TrackingNumber { get; private set; }

    /// <summary>Kdy dopravce balík převzal (přidělil číslo zásilky).</summary>
    public DateTime? ShippedAtUtc { get; private set; }

    public DateTime? DeliveredAtUtc { get; private set; }

    /// <summary>Dopravce balík nedoručil a vrátil ho (kompenzace jen jeho části = krok 28D).</summary>
    public DateTime? ReturnedToSenderAtUtc { get; private set; }

    /// <summary>Proč dopravce balík naposledy odmítl, nebo null.</summary>
    public string? ShipmentError { get; private set; }

    /// <summary>Předán dopravci a ještě nepřišlo číslo zásilky ani odmítnutí.</summary>
    public bool IsAwaitingShipment => ShipmentRequestedAtUtc is not null && ShippedAtUtc is null;

    public bool IsShipped => ShippedAtUtc is not null;

    /// <summary>Konečný stav balíku – doručen, nebo vrácen dopravcem.</summary>
    public bool IsFinished => DeliveredAtUtc is not null || ReturnedToSenderAtUtc is not null;

    internal void RequestShipment(DateTime requestedAtUtc)
    {
        if (IsShipped)
        {
            throw new DomainException($"Balík {Number} už je u dopravce.");
        }

        if (IsAwaitingShipment)
        {
            throw new DomainException($"Balík {Number} se už dopravci předává.");
        }

        ShipmentRequestedAtUtc = requestedAtUtc;
        ShipmentError = null;
    }

    internal bool ConfirmShipment(string carrier, string trackingNumber, DateTime shippedAtUtc)
    {
        if (!IsAwaitingShipment)
        {
            return false;   // opožděná nebo opakovaná odpověď
        }

        Carrier = carrier;
        TrackingNumber = trackingNumber;
        ShippedAtUtc = shippedAtUtc;
        ShipmentError = null;
        return true;
    }

    internal bool FailShipment(string reason)
    {
        if (!IsAwaitingShipment)
        {
            return false;
        }

        ShipmentRequestedAtUtc = null;
        ShipmentError = reason.Length > Order.ShipmentErrorMaxLength ? reason[..Order.ShipmentErrorMaxLength] : reason;
        return true;
    }

    internal bool MarkDelivered(DateTime deliveredAtUtc)
    {
        if (!IsShipped || IsFinished)
        {
            return false;
        }

        DeliveredAtUtc = deliveredAtUtc;
        return true;
    }

    internal bool MarkReturnedToSender(DateTime returnedAtUtc)
    {
        if (!IsShipped || IsFinished)
        {
            return false;
        }

        ReturnedToSenderAtUtc = returnedAtUtc;
        return true;
    }
}

/// <summary>Kolik kusů produktu je v balíku.</summary>
public sealed record OrderParcelLine(int ProductId, int Quantity);

/// <summary>Balík tak, jak ho sklad poslal ve zprávě – vstup pro <see cref="Order.SetParcels"/>.</summary>
public sealed record ParcelPlan(string WarehouseCode, string WarehouseName, IReadOnlyList<OrderParcelLine> Lines);
