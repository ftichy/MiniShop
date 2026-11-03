using MiniShop.Domain.Common;
using MiniShop.Domain.Exceptions;
using MiniShop.Domain.Orders.Events;

namespace MiniShop.Domain.Orders;

/// <summary>
/// Objednávka zákazníka. Položky lze přidávat jen přes <see cref="AddItem"/>,
/// stav se mění jen přes <see cref="Confirm"/>, <see cref="Reject"/>, <see cref="StartPacking"/>, <see cref="Ship"/>,
/// <see cref="MarkDelivered"/> a <see cref="Cancel"/>.
/// Každá změna stavu vyvolá doménovou událost a přidá řádek do <see cref="StatusHistory"/> (e-shop krok 5).
/// </summary>
/// <remarks>
/// Od kroku 9 objednávka vzniká jako Pending a o potvrzení rozhoduje sága (OrderSaga) podle odpovědi skladu.
/// Objednávka sama sklad nezná – jen přijme výsledek.
/// </remarks>
public class Order : Entity
{
    private readonly List<OrderItem> _items = new();
    private readonly List<OrderStatusChange> _statusHistory = new();

    // Konstruktor pro EF Core. EF při načítání z DB volá tento (bezparametrický) konstruktor,
    // takže se při načtení objednávky událost OrderPlaced znovu nevyvolá.
    private Order()
    {
    }

    /// <param name="customerId">
    /// Účty krok 3: kdo objednává – ID uživatelského účtu. Pro Ordering je to jen neprůhledný řetězec
    /// (o modulu účtů nic neví). Jméno a e-mail jsou kopie z doby objednání – pozdější změna účtu je nepřepíše.
    /// </param>
    /// <param name="checkoutId">
    /// E-shop krok 4: ID odeslání z pokladny (nebo z API) – stejné ID podruhé nezaloží druhou objednávku
    /// (unikátní index). Null = objednávka bez něj (starší, API bez <c>checkoutId</c>).
    /// </param>
    public Order(string customerId, string customerName, string customerEmail, DateTime createdAtUtc, Guid? checkoutId = null)
    {
        if (string.IsNullOrWhiteSpace(customerId))
        {
            throw new DomainException("Objednávka musí patřit zákazníkovi.");
        }

        if (string.IsNullOrWhiteSpace(customerName))
        {
            throw new DomainException("Jméno zákazníka je povinné.");
        }

        if (string.IsNullOrWhiteSpace(customerEmail))
        {
            throw new DomainException("E-mail zákazníka je povinný.");
        }

        CustomerId = customerId;
        CustomerName = customerName.Trim();
        CustomerEmail = customerEmail.Trim();
        CreatedAtUtc = createdAtUtc;
        CheckoutId = checkoutId;
        ChangeStatus(OrderStatus.Pending, createdAtUtc);

        // Položky se přidají až po konstruktoru, ale událost drží odkaz na objednávku,
        // takže handler (krok 2) uvidí objednávku i s položkami.
        Raise(new OrderPlaced(this, createdAtUtc));
    }

    public int Id { get; private set; }

    /// <summary>Účty krok 3: vlastník objednávky (ID uživatelského účtu).</summary>
    public string CustomerId { get; private set; } = string.Empty;

    /// <summary>Patří objednávka tomuto zákazníkovi?</summary>
    public bool BelongsTo(string customerId) => CustomerId == customerId;

    public string CustomerName { get; private set; } = string.Empty;
    public string CustomerEmail { get; private set; } = string.Empty;
    public DateTime CreatedAtUtc { get; private set; }
    public OrderStatus Status { get; private set; }

    /// <summary>Proč byla objednávka zamítnuta (jen ve stavu Rejected).</summary>
    public string? RejectionReason { get; private set; }

    public IReadOnlyCollection<OrderItem> Items => _items.AsReadOnly();

    /// <summary>
    /// E-shop krok 5: historie stavů v pořadí, jak nastaly (první = Pending při vzniku). Zvenku jen ke čtení –
    /// řádek přidá jen změna stavu.
    /// </summary>
    public IReadOnlyCollection<OrderStatusChange> StatusHistory => _statusHistory.AsReadOnly();

    /// <summary>E-shop krok 4: ID odeslání z pokladny – idempotence (dvojklik, F5 nevytvoří druhou objednávku).</summary>
    public Guid? CheckoutId { get; private set; }

    /// <summary>E-shop krok 4: způsob dopravy (None = objednávka bez pokladny).</summary>
    public ShippingMethod ShippingMethod { get; private set; }

    /// <summary>E-shop krok 4: cena dopravy v době objednání (snímek, jako ceny položek).</summary>
    public decimal ShippingPrice { get; private set; }

    /// <summary>E-shop krok 4: doručovací adresa – jen u kurýra. Osobní údaj: smaže ji <see cref="Anonymize"/>.</summary>
    public DeliveryAddress? DeliveryAddress { get; private set; }

    /// <summary>E-shop krok 6: dopravce (jen kurýr; vyplní <see cref="Ship"/>).</summary>
    public string? Carrier { get; private set; }

    /// <summary>E-shop krok 6: číslo zásilky u dopravce (jen kurýr).</summary>
    public string? TrackingNumber { get; private set; }

    /// <summary>
    /// E-shop krok 6: pojistka proti souběhu (concurrency token) – každá změna stavu ji zvýší. Admin odesílá a zákazník
    /// ruší ve stejné chvíli → druhý zápis (UPDATE … WHERE Version = původní) selže a nic se nepřepíše.
    /// </summary>
    public int Version { get; private set; }

    /// <summary>Součet položek po slevách (bez dopravy).</summary>
    public decimal ItemsTotal => _items.Sum(i => i.LineTotal);

    /// <summary>Celkem k zaplacení – položky + doprava.</summary>
    public decimal TotalPrice => ItemsTotal + ShippingPrice;

    /// <summary>
    /// E-shop krok 4: doprava z pokladny. Kurýr potřebuje adresu, osobní odběr žádnou (ani ji neuloží).
    /// Cenu spočítá aplikační vrstva (strategie), objednávka si ji jen zapamatuje.
    /// </summary>
    public void SetShipping(ShippingMethod method, decimal price, DeliveryAddress? address)
    {
        EnsureStatus(OrderStatus.Pending, "Dopravu lze zadat jen u nové objednávky.");
        if (method == ShippingMethod.None)
        {
            throw new DomainException("Vyberte způsob dopravy.");
        }

        if (method == ShippingMethod.Courier && address is null)
        {
            throw new DomainException("Pro doručení kurýrem vyplňte doručovací adresu.");
        }

        if (price < 0)
        {
            throw new DomainException("Cena dopravy nesmí být záporná.");
        }

        ShippingMethod = method;
        ShippingPrice = price;
        DeliveryAddress = method == ShippingMethod.Courier ? address : null;
    }

    /// <summary>Název a cena se berou z lokální kopie katalogu (krok 10) a uloží se jako snímek v položce.</summary>
    public void AddItem(CatalogProduct product, int quantity, decimal discount)
    {
        EnsureStatus(OrderStatus.Pending, "Položky lze přidávat jen do nové objednávky.");
        if (product.IsRemoved)
        {
            throw new DomainException($"Produkt {product.Name} už není v nabídce.");
        }

        _items.Add(new OrderItem(product.Id, product.Name, product.Price, quantity, discount));
    }

    /// <summary>Sklad zboží zarezervoval (krok 9).</summary>
    public void Confirm(DateTime confirmedAtUtc)
    {
        EnsureStatus(OrderStatus.Pending, "Potvrdit lze jen čekající objednávku.");
        ChangeStatus(OrderStatus.Confirmed, confirmedAtUtc);
        Raise(new OrderConfirmed(this, confirmedAtUtc));
    }

    /// <summary>Sklad zboží nezarezervoval nebo neodpověděl včas (krok 9).</summary>
    public void Reject(string reason, DateTime rejectedAtUtc)
    {
        EnsureStatus(OrderStatus.Pending, "Zamítnout lze jen čekající objednávku.");
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new DomainException("Důvod zamítnutí je povinný.");
        }

        RejectionReason = reason.Trim();
        ChangeStatus(OrderStatus.Rejected, rejectedAtUtc, RejectionReason);
        Raise(new OrderRejected(this, rejectedAtUtc));
    }

    /// <summary>E-shop krok 6: sklad začal objednávku balit (osobní odběr: připravovat).</summary>
    public void StartPacking(DateTime startedAtUtc)
    {
        EnsureStatus(OrderStatus.Confirmed, "Balit lze jen potvrzenou objednávku.");
        ChangeStatus(OrderStatus.Packing, startedAtUtc);
        Raise(new OrderPackingStarted(this, startedAtUtc));
    }

    /// <summary>
    /// E-shop krok 6: odeslání. Kurýr potřebuje dopravce a číslo zásilky; osobní odběr = „připraveno k vyzvednutí“
    /// (dopravce ani číslo nemá). Od této chvíle objednávku zrušit nejde.
    /// </summary>
    public void Ship(string? carrier, string? trackingNumber, DateTime shippedAtUtc)
    {
        EnsureStatus(OrderStatus.Packing, "Odeslat lze jen zabalenou objednávku.");
        if (ShippingMethod == ShippingMethod.Pickup)
        {
            carrier = null;
            trackingNumber = null;
        }
        else if (ShippingMethod == ShippingMethod.Courier
            && (string.IsNullOrWhiteSpace(carrier) || string.IsNullOrWhiteSpace(trackingNumber)))
        {
            throw new DomainException("Pro odeslání kurýrem vyplňte dopravce a číslo zásilky.");
        }

        Carrier = string.IsNullOrWhiteSpace(carrier) ? null : carrier.Trim();
        TrackingNumber = string.IsNullOrWhiteSpace(trackingNumber) ? null : trackingNumber.Trim();
        ChangeStatus(OrderStatus.Shipped, shippedAtUtc,
            Carrier is null ? null : $"{Carrier}{(TrackingNumber is null ? "" : $", zásilka {TrackingNumber}")}");
        Raise(new OrderShipped(this, shippedAtUtc));
    }

    /// <summary>
    /// E-shop krok 6: zásilka doručena (osobní odběr: vyzvednuta). Dřív „Complete“ přímo z Confirmed – teď až po odeslání.
    /// Událost zůstává <see cref="OrderCompleted"/> kvůli kontraktu OrderCompletedV1.
    /// </summary>
    public void MarkDelivered(DateTime deliveredAtUtc)
    {
        EnsureStatus(OrderStatus.Shipped, "Doručit lze jen odeslanou objednávku.");
        ChangeStatus(OrderStatus.Completed, deliveredAtUtc);
        Raise(new OrderCompleted(this, deliveredAtUtc));
    }

    /// <summary>
    /// Zrušení potvrzené nebo balené objednávky – sága pak uvolní rezervaci skladu (kompenzace).
    /// E-shop krok 6: po odeslání už ne (zboží je na cestě).
    /// </summary>
    public void Cancel(DateTime cancelledAtUtc)
    {
        if (Status is not (OrderStatus.Confirmed or OrderStatus.Packing))
        {
            throw new DomainException(Status == OrderStatus.Shipped
                ? "Odeslanou objednávku už zrušit nejde."
                : "Zrušit lze jen potvrzenou objednávku, která ještě neodešla.");
        }

        ChangeStatus(OrderStatus.Cancelled, cancelledAtUtc);
        Raise(new OrderCancelled(this, cancelledAtUtc));
    }

    /// <summary>Jméno smazaného zákazníka na jeho objednávkách (účty krok 9).</summary>
    public const string AnonymizedCustomerName = "Smazaný zákazník";

    /// <summary>Je objednávka rozpracovaná (čeká na sklad, balí se nebo je na cestě)?</summary>
    public bool IsActive => Status is OrderStatus.Pending or OrderStatus.Confirmed or OrderStatus.Packing or OrderStatus.Shipped;

    /// <summary>
    /// Účty krok 9: zákazník smazal účet – objednávka zůstane (položky, ceny, stav pro účetnictví a statistiky),
    /// ale bez jména a e-mailu. Rozpracovanou objednávku anonymizovat nejde – nebylo by komu ji doručit.
    /// </summary>
    /// <remarks>Bez doménové události: zákazníkovi už se nic neposílá a nikoho jiného to nezajímá.</remarks>
    public void Anonymize()
    {
        if (IsActive)
        {
            throw new DomainException("Rozpracovanou objednávku nejde anonymizovat.");
        }

        CustomerName = AnonymizedCustomerName;
        CustomerEmail = string.Empty;
        DeliveryAddress = null;   // e-shop krok 4: adresa je osobní údaj
    }

    /// <summary>E-shop krok 5: jediné místo, kde se mění stav – nový stav a řádek historie vždy spolu.</summary>
    private void ChangeStatus(OrderStatus status, DateTime changedAtUtc, string? note = null)
    {
        Status = status;
        _statusHistory.Add(new OrderStatusChange(status, changedAtUtc, note));
        Version++;   // e-shop krok 6: souběžná změna stejné objednávky pak při uložení selže
    }

    private void EnsureStatus(OrderStatus expected, string message)
    {
        if (Status != expected)
        {
            throw new DomainException(message);
        }
    }
}
