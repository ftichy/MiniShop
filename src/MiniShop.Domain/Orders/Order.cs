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
    private readonly List<OrderReturn> _returns = new();

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

    /// <summary>E-shop krok 8: způsob platby (None = objednávka bez pokladny, bez platby).</summary>
    public PaymentMethod PaymentMethod { get; private set; }

    /// <summary>E-shop krok 6: dopravce (jen kurýr; vyplní <see cref="Ship"/>).</summary>
    public string? Carrier { get; private set; }

    /// <summary>E-shop krok 6: číslo zásilky u dopravce (jen kurýr).</summary>
    public string? TrackingNumber { get; private set; }

    /// <summary>
    /// E-shop krok 26B: kdy skladník předal objednávku dopravci (<see cref="RequestShipment"/>) – čeká se na číslo zásilky.
    /// Null = nepředáno, nebo dopravce zásilku odmítl (<see cref="ShipmentError"/>).
    /// </summary>
    public DateTime? ShipmentRequestedAtUtc { get; private set; }

    /// <summary>E-shop krok 26B: proč dopravce zásilku naposledy odmítl (česká hláška pro Expedici), nebo null.</summary>
    public string? ShipmentError { get; private set; }

    public const int ShipmentErrorMaxLength = 300;

    /// <summary>E-shop krok 26B: předává se dopravci a ještě nepřišlo číslo zásilky ani odmítnutí.</summary>
    public bool IsAwaitingShipment => Status == OrderStatus.Packing && ShipmentRequestedAtUtc is not null;

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

    /// <summary>E-shop krok 13: použitý slevový kód (ID kuponu – kvůli vrácení použití), nebo null.</summary>
    public int? CouponId { get; private set; }

    /// <summary>E-shop krok 13: slevový kód, jak ho zákazník uplatnil (pro zobrazení).</summary>
    public string? CouponCode { get; private set; }

    /// <summary>E-shop krok 13: kolik kupon odečetl (už je rozpočítané ve slevách položek).</summary>
    public decimal CouponDiscount { get; private set; }

    /// <summary>
    /// E-shop krok 13: uplatní slevový kód. Sleva se <b>rozpočítá do slev položek</b> (poměrně podle ceny řádku,
    /// haléřový zbytek na poslední řádek) – <see cref="ItemsTotal"/> i <see cref="TotalPrice"/> jsou pak po slevě a žádný
    /// kontrakt (OrderPlacedV2, vrácení peněz) se nemusí měnit. Volat po položkách a před dopravou (doprava zdarma
    /// se počítá z ceny po slevě). Kupon si sám zvýší počet použití (<see cref="Coupons.Coupon.Redeem"/>).
    /// </summary>
    public void ApplyCoupon(Coupons.Coupon coupon, DateTime nowUtc)
    {
        EnsureStatus(OrderStatus.Pending, "Slevový kód lze uplatnit jen u nové objednávky.");
        if (CouponId is not null)
        {
            throw new DomainException("Na objednávku jde uplatnit jen jeden slevový kód.");
        }

        if (_items.Count == 0)
        {
            throw new DomainException("Slevový kód potřebuje položky objednávky.");
        }

        var itemsTotal = ItemsTotal;
        var discount = coupon.Redeem(itemsTotal, nowUtc);
        // 1. poměrně, zaokrouhleno dolů na haléře; 2. haléřový zbytek od nejdražšího řádku, kolik se vejde
        //    (sleva v Kč může pokrýt celé zboží – žádný řádek nesmí jít pod nulu).
        var shares = _items.ToDictionary(i => i,
            i => decimal.Round(discount * i.LineTotal / itemsTotal, 2, MidpointRounding.ToZero));
        var remaining = discount - shares.Values.Sum();
        foreach (var item in _items.OrderByDescending(i => i.LineTotal))
        {
            var extra = Math.Min(remaining, item.LineTotal - shares[item]);
            shares[item] += extra;
            remaining -= extra;
        }

        foreach (var (item, share) in shares)
        {
            item.AddDiscount(share);
        }

        CouponId = coupon.Id;
        CouponCode = coupon.Code;
        CouponDiscount = discount;
    }

    /// <summary>E-shop krok 8: způsob platby z pokladny (kartou / dobírka). Jen u nové objednávky.</summary>
    public void SetPayment(PaymentMethod method)
    {
        EnsureStatus(OrderStatus.Pending, "Platbu lze zvolit jen u nové objednávky.");
        if (method == PaymentMethod.None)
        {
            throw new DomainException("Vyberte způsob platby.");
        }

        PaymentMethod = method;
    }

    /// <summary>Platí se kartou přes bránu – sága po rezervaci čeká na platbu (e-shop krok 8).</summary>
    public bool RequiresOnlinePayment => PaymentMethod == PaymentMethod.Card;

    /// <summary>E-shop krok 8: zboží je zarezervované, čeká se na platbu kartou (rozhoduje sága).</summary>
    public void AwaitPayment(DateTime atUtc)
    {
        EnsureStatus(OrderStatus.Pending, "Na platbu může čekat jen nová objednávka.");
        if (!RequiresOnlinePayment)
        {
            throw new DomainException("Objednávka se neplatí kartou.");
        }

        ChangeStatus(OrderStatus.AwaitingPayment, atUtc);
        Raise(new OrderAwaitingPayment(this, atUtc));
    }

    /// <summary>
    /// Sklad zboží zarezervoval (krok 9) – u platby kartou až po zaplacení (e-shop krok 8: z AwaitingPayment).
    /// </summary>
    public void Confirm(DateTime confirmedAtUtc)
    {
        var allowed = RequiresOnlinePayment ? OrderStatus.AwaitingPayment : OrderStatus.Pending;
        EnsureStatus(allowed, RequiresOnlinePayment
            ? "Potvrdit lze jen zaplacenou objednávku."
            : "Potvrdit lze jen čekající objednávku.");
        ChangeStatus(OrderStatus.Confirmed, confirmedAtUtc);
        Raise(new OrderConfirmed(this, confirmedAtUtc));
    }

    /// <summary>
    /// Sklad zboží nezarezervoval nebo neodpověděl včas (krok 9). E-shop krok 8: i když platba neproběhla
    /// (z AwaitingPayment) – „systém ji nemohl vyřídit“, ne „někdo ji zrušil“.
    /// </summary>
    public void Reject(string reason, DateTime rejectedAtUtc)
    {
        if (Status is not (OrderStatus.Pending or OrderStatus.AwaitingPayment))
        {
            throw new DomainException("Zamítnout lze jen čekající objednávku.");
        }

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
        if (IsAwaitingShipment)
        {
            throw new DomainException("Zásilka se předává dopravci – počkejte na číslo zásilky.");
        }

        ShipNow(carrier, trackingNumber, shippedAtUtc);
    }

    /// <summary>
    /// E-shop krok 26B: skladník předává zabalenou objednávku dopravci. Číslo zásilky přidělí dopravce – přijde zprávou
    /// (<see cref="ConfirmShipment"/>), nebo odmítnutí (<see cref="FailShipment"/>). Dvojklik / souběh = podruhé chyba.
    /// </summary>
    public void RequestShipment(DateTime requestedAtUtc)
    {
        EnsureStatus(OrderStatus.Packing, "Dopravci lze předat jen zabalenou objednávku.");
        if (ShippingMethod != ShippingMethod.Courier || DeliveryAddress is null)
        {
            throw new DomainException("Dopravci se předává jen zásilka kurýrem s doručovací adresou.");
        }

        if (ShipmentRequestedAtUtc is not null)
        {
            throw new DomainException("Zásilka se už dopravci předává.");
        }

        ShipmentRequestedAtUtc = requestedAtUtc;
        ShipmentError = null;
        Version++;   // bez změny stavu – souběh (dvojklik, zákazník ruší) ale zastavit musí
        Raise(new OrderShipmentRequested(this, requestedAtUtc));
    }

    /// <summary>
    /// E-shop krok 26B: dopravce zásilku převzal do systému (ShipmentCreatedV1) → odeslat. Opakovaná nebo opožděná zpráva
    /// (objednávka už odeslaná, mezitím ručně jiným dopravcem) vrátí false a nic nezmění.
    /// </summary>
    public bool ConfirmShipment(string carrier, string trackingNumber, DateTime shippedAtUtc)
    {
        if (!IsAwaitingShipment)
        {
            return false;
        }

        ShipmentError = null;
        ShipNow(carrier, trackingNumber, shippedAtUtc);
        return true;
    }

    /// <summary>E-shop krok 26B: dopravce zásilku odmítl (ShipmentFailedV1) → zpět do „K odeslání“ s hláškou.</summary>
    public bool FailShipment(string reason)
    {
        if (!IsAwaitingShipment)
        {
            return false;
        }

        ShipmentRequestedAtUtc = null;
        ShipmentError = reason.Length > ShipmentErrorMaxLength ? reason[..ShipmentErrorMaxLength] : reason;
        Version++;
        return true;
    }

    /// <summary>E-shop krok 26C: zásilku předal dopravce (číslo zásilky od něj) – doručení pak hlásí on, ne skladník.</summary>
    public bool IsShippedByCarrier => ShipmentRequestedAtUtc is not null && Status != OrderStatus.Packing;

    /// <summary>
    /// E-shop krok 26C: dopravce ohlásil doručení (ShipmentDeliveredV1). Už doručená (ručně „Doručeno“ dřív) nebo jiný
    /// stav = false, nic se nemění – zpráva může přijít pozdě nebo podruhé.
    /// </summary>
    public bool MarkDeliveredByCarrier(DateTime deliveredAtUtc)
    {
        if (Status != OrderStatus.Shipped)
        {
            return false;
        }

        MarkDelivered(deliveredAtUtc);
        return true;
    }

    private void ShipNow(string? carrier, string? trackingNumber, DateTime shippedAtUtc)
    {
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
    /// Zrušení kdykoli před odesláním – e-shop krok 9: i objednávky, která čeká na sklad nebo na platbu. Co je potřeba
    /// vrátit (zboží, peníze), dožene sága – i když odpověď skladu nebo brány teprve přijde (kompenzace).
    /// E-shop krok 6: po odeslání už ne (zboží je na cestě).
    /// </summary>
    public void Cancel(DateTime cancelledAtUtc)
    {
        if (Status is not (OrderStatus.Pending or OrderStatus.AwaitingPayment or OrderStatus.Confirmed or OrderStatus.Packing))
        {
            throw new DomainException(Status is OrderStatus.Shipped or OrderStatus.Completed
                ? "Odeslanou objednávku už zrušit nejde."
                : "Tuto objednávku už zrušit nejde.");
        }

        // E-shop krok 26B: zásilka už může u dopravce existovat a simulátor storno neumí (krok 26D).
        if (IsAwaitingShipment)
        {
            throw new DomainException("Objednávka se předává dopravci – zrušit už nejde.");
        }

        ChangeStatus(OrderStatus.Cancelled, cancelledAtUtc);
        Raise(new OrderCancelled(this, cancelledAtUtc));
    }

    public const int ReturnReasonMaxLength = 500;

    /// <summary>E-shop krok 12: proč zákazník vrací (z formuláře), nebo null.</summary>
    public string? ReturnReason { get; private set; }

    /// <summary>E-shop krok 12: proč vrácení neprošlo (admin zamítl, zboží nedorazilo včas), nebo null.</summary>
    public string? ReturnRejectionReason { get; private set; }

    /// <summary>E-shop krok 12: kdy byla objednávka doručena (první přechod do Completed), nebo null.</summary>
    public DateTime? DeliveredAtUtc => _statusHistory
        .Where(h => h.Status == OrderStatus.Completed && h.ChangedAtUtc is not null)
        .Min(h => h.ChangedAtUtc);

    /// <summary>
    /// E-shop krok 12: jde objednávku ještě vrátit? Jen doručenou, do <paramref name="window"/> od doručení a jen jednou
    /// (zamítnutá nebo vypršelá žádost se neopakuje). Starší objednávky bez času doručení ne.
    /// </summary>
    public bool CanRequestReturn(DateTime nowUtc, TimeSpan window) =>
        Status == OrderStatus.Completed
        && DeliveredAtUtc is { } delivered && nowUtc <= delivered + window
        && _statusHistory.All(h => h.Status != OrderStatus.ReturnRequested)
        && !_returns.Any(r => r.CountsAsReturned);   // e-shop krok 22: po vratce po částech už jen další vratka po částech

    /// <summary>
    /// E-shop krok 12: zákazník chce zboží vrátit. Objednávka čeká na zboží; co dál (naskladnění, peníze), řídí sága vratky.
    /// </summary>
    public void RequestReturn(string reason, DateTime requestedAtUtc, TimeSpan window)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new DomainException("Napište prosím, proč zboží vracíte.");
        }

        if (!CanRequestReturn(requestedAtUtc, window))
        {
            throw new DomainException(Status == OrderStatus.Completed
                ? $"Vrátit lze jen jednou a do {window.TotalDays:0} dní od doručení."
                : "Vrátit lze jen doručenou objednávku.");
        }

        ReturnReason = reason.Trim()[..Math.Min(reason.Trim().Length, ReturnReasonMaxLength)];
        ChangeStatus(OrderStatus.ReturnRequested, requestedAtUtc, ReturnReason);
        Raise(new OrderReturnRequested(this, requestedAtUtc));
    }

    /// <summary>E-shop krok 12: vrácení neprošlo (admin zamítl, zboží nedorazilo včas) – objednávka zůstává doručená.</summary>
    public void RejectReturn(string reason, DateTime rejectedAtUtc)
    {
        EnsureStatus(OrderStatus.ReturnRequested, "Zamítnout lze jen čekající vrácení.");
        ReturnRejectionReason = reason;
        ChangeStatus(OrderStatus.Completed, rejectedAtUtc, reason);
        Raise(new OrderReturnRejected(this, rejectedAtUtc));
    }

    /// <summary>E-shop krok 12: zboží je zpět na skladu (a peníze vrácené) – objednávka je vrácená.</summary>
    public void CompleteReturn(DateTime returnedAtUtc)
    {
        EnsureStatus(OrderStatus.ReturnRequested, "Dokončit lze jen čekající vrácení.");
        ChangeStatus(OrderStatus.Returned, returnedAtUtc);
        Raise(new OrderReturned(this, returnedAtUtc));
    }

    /// <summary>E-shop krok 22: vratky po částech v pořadí, jak vznikaly (číslo 1, 2, …).</summary>
    public IReadOnlyCollection<OrderReturn> Returns => _returns.AsReadOnly();

    /// <summary>E-shop krok 22: rozpracovaná vratka po částech (nejvýš jedna najednou), nebo null.</summary>
    public OrderReturn? OpenReturn => _returns.FirstOrDefault(r => r.IsOpen);

    /// <summary>E-shop krok 22: kolik kusů produktu jde ještě vrátit – koupené bez vrácených a právě vracených.</summary>
    public int ReturnableQuantity(int productId) =>
        _items.Where(i => i.ProductId == productId).Sum(i => i.Quantity) - ReturnedQuantity(productId);

    /// <summary>
    /// E-shop krok 22: jde ještě vrátit část? Jen doručenou objednávku, do <paramref name="window"/> od doručení, když žádná
    /// vratka zrovna neběží, zbývá aspoň kus a nebyla už vratka celé objednávky. Zamítnutá vratka po částech další nebrání.
    /// </summary>
    public bool CanRequestPartialReturn(DateTime nowUtc, TimeSpan window) =>
        Status == OrderStatus.Completed
        && DeliveredAtUtc is { } delivered && nowUtc <= delivered + window
        && OpenReturn is null
        && _statusHistory.All(h => h.Status != OrderStatus.ReturnRequested)   // po vratce celé objednávky (i zamítnuté) už ne
        && _items.Any(i => ReturnableQuantity(i.ProductId) > 0);

    /// <summary>
    /// E-shop krok 22: zákazník vrací vybrané kusy (<paramref name="quantities"/>: produkt → kusy). Když jako první vratku
    /// vybere všechno, jde o vratku celé objednávky – dnešní cesta (<see cref="RequestReturn(string, DateTime, TimeSpan)"/>,
    /// vrací se i doprava). Jinak vznikne <see cref="OrderReturn"/> a objednávka zůstává doručená.
    /// </summary>
    /// <returns>Číslo vratky; 0 = vratka celé objednávky.</returns>
    public int RequestReturn(string reason, IReadOnlyDictionary<int, int> quantities, DateTime requestedAtUtc, TimeSpan window)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new DomainException("Napište prosím, proč zboží vracíte.");
        }

        var selected = quantities.Where(q => q.Value > 0).ToDictionary(q => q.Key, q => q.Value);
        if (selected.Count == 0)
        {
            throw new DomainException("Vyberte prosím aspoň jeden kus, který vracíte.");
        }

        var products = _items.GroupBy(i => i.ProductId).ToDictionary(g => g.Key, g => g.ToList());
        var unknown = selected.Keys.FirstOrDefault(id => !products.ContainsKey(id));
        if (unknown != 0)
        {
            throw new DomainException($"Produkt {unknown} v objednávce není.");
        }

        if (CanRequestReturn(requestedAtUtc, window)
            && products.All(p => selected.GetValueOrDefault(p.Key) == p.Value.Sum(i => i.Quantity)))
        {
            RequestReturn(reason, requestedAtUtc, window);
            return 0;
        }

        if (!CanRequestPartialReturn(requestedAtUtc, window))
        {
            throw new DomainException(Status != OrderStatus.Completed
                ? "Vrátit lze jen doručenou objednávku."
                : OpenReturn is not null
                    ? "Předchozí vratka se ještě vyřizuje – další můžete poslat, až skončí."
                    : _statusHistory.Any(h => h.Status == OrderStatus.ReturnRequested)
                        ? "Objednávku už jste jednou celou vraceli – další vratka nejde."
                        : _items.All(i => ReturnableQuantity(i.ProductId) == 0)
                            ? "Všechno zboží z objednávky už je vrácené."
                            : $"Vrátit lze do {window.TotalDays:0} dní od doručení.");
        }

        var lines = new List<OrderReturnLine>();
        foreach (var (productId, quantity) in selected)
        {
            var items = products[productId];
            var remaining = ReturnableQuantity(productId);
            if (quantity > remaining)
            {
                throw new DomainException($"Produktu {items[0].ProductName} můžete vrátit nejvýš {remaining} ks.");
            }

            // Sleva z kuponu je rozpočítaná do položek – na vrácené kusy připadne poměrný díl, poslední kusy dostanou zbytek
            // (součet slev všech vratek = sleva položky, nic se neztratí ani nepřidá).
            var bought = items.Sum(i => i.Quantity);
            var discount = items.Sum(i => i.Discount);
            var discountReturned = _returns.Where(r => r.CountsAsReturned).SelectMany(r => r.Lines)
                .Where(l => l.ProductId == productId).Sum(l => l.Discount);
            var share = quantity == remaining
                ? discount - discountReturned
                : Math.Round(discount * quantity / bought, 2, MidpointRounding.AwayFromZero);
            lines.Add(new OrderReturnLine(productId, items[0].ProductName, quantity, items[0].UnitPrice, share));
        }

        var orderReturn = OrderReturn.Request(_returns.Count + 1, reason, lines, requestedAtUtc);
        _returns.Add(orderReturn);
        Version++;   // souběh: dva formuláře naráz – druhý zápis selže na verzi objednávky
        Raise(new OrderPartialReturnRequested(this, orderReturn.Number, requestedAtUtc));
        return orderReturn.Number;
    }

    /// <summary>E-shop krok 22: vratka po částech je hotová (kusy na skladu, peníze vrácené) – objednávka zůstává doručená.</summary>
    public void CompletePartialReturn(int returnNumber, DateTime completedAtUtc)
    {
        GetReturn(returnNumber).Complete(completedAtUtc);
        Version++;
        Raise(new OrderPartialReturnCompleted(this, returnNumber, completedAtUtc));
    }

    /// <summary>E-shop krok 22: vratka po částech neprošla (admin zamítl, zboží nedorazilo včas).</summary>
    public void RejectPartialReturn(int returnNumber, string reason, DateTime rejectedAtUtc)
    {
        GetReturn(returnNumber).Reject(reason, rejectedAtUtc);
        Version++;
        Raise(new OrderPartialReturnRejected(this, returnNumber, rejectedAtUtc));
    }

    /// <summary>E-shop krok 22: vratka podle čísla.</summary>
    public OrderReturn GetReturn(int returnNumber) =>
        _returns.FirstOrDefault(r => r.Number == returnNumber)
        ?? throw new DomainException($"Vratka {returnNumber} objednávky {Id} neexistuje.");

    /// <summary>
    /// E-shop krok 22: produkty, ze kterých si zákazník po dokončených vratkách nenechal ani kus (pro Reviews).
    /// </summary>
    public IReadOnlyList<int> FullyReturnedProductIds() =>
        _items.Select(i => i.ProductId).Distinct()
            .Where(id => _returns.Where(r => r.Status == OrderReturnStatus.Completed).SelectMany(r => r.Lines)
                .Where(l => l.ProductId == id).Sum(l => l.Quantity) >= _items.Where(i => i.ProductId == id).Sum(i => i.Quantity))
            .ToList();

    private int ReturnedQuantity(int productId) =>
        _returns.Where(r => r.CountsAsReturned).SelectMany(r => r.Lines).Where(l => l.ProductId == productId).Sum(l => l.Quantity);

    /// <summary>Jméno smazaného zákazníka na jeho objednávkách (účty krok 9).</summary>
    public const string AnonymizedCustomerName = "Smazaný zákazník";

    /// <summary>Je objednávka rozpracovaná (čeká na sklad, balí se, je na cestě nebo se vrací)?</summary>
    public bool IsActive => Status is OrderStatus.Pending or OrderStatus.AwaitingPayment or OrderStatus.Confirmed
        or OrderStatus.Packing or OrderStatus.Shipped or OrderStatus.ReturnRequested
        || OpenReturn is not null;   // e-shop krok 22: rozpracovaná vratka po částech

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
