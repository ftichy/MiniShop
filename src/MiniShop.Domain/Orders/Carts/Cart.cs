using MiniShop.Domain.Exceptions;

namespace MiniShop.Domain.Orders.Carts;

/// <summary>
/// E-shop krok 3: nákupní košík zákazníka – agregát s vlastními pravidly (invarianty): nejvýš <see cref="MaxLines"/>
/// řádků, množství 1–<see cref="MaxQuantity"/>, každý produkt jen jednou (další přidání = navýšení množství).
/// Řádky se mění jen přes metody košíku, takže pravidla nejde obejít.
/// </summary>
/// <remarks>
/// <para>
/// <b>E-shop krok 17 – event sourcing:</b> zdrojem pravdy jsou události (<see cref="CartEvent"/>). Každá metoda
/// nejdřív ověří pravidlo, pak vyvolá událost (<see cref="Raise"/>) a teprve <see cref="Apply"/> změní stav.
/// Načtený košík si stav přehraje ze svých událostí (<see cref="Rehydrate"/>). Tabulky <c>Carts</c> a <c>CartLines</c>
/// zůstávají jako <b>projekce</b> (čtecí model) pro rychlé dotazy – odznak v hlavičce, opuštěné košíky.
/// </para>
/// <para>
/// Košík neblokuje zboží – rezervace vzniká až u pokladny (krok 4). Cena v košíku je jen orientační:
/// zobrazuje se vždy aktuální z kopie katalogu, <see cref="CartLine.PriceWhenAdded"/> slouží jen k upozornění na změnu.
/// Doménové události (outbox) nevyvolává; opuštěný košík (krok 10) oznamuje aplikační vrstva.
/// </para>
/// </remarks>
public sealed class Cart
{
    public const int MaxLines = 20;
    public const int MaxQuantity = 100;

    private readonly List<CartLine> _lines = new();
    private readonly List<CartEvent> _uncommittedEvents = new();

    // Konstruktor pro EF Core a pro přehrání událostí.
    private Cart()
    {
    }

    /// <param name="customerId">ID uživatelského účtu – pro Ordering neprůhledný řetězec (jako u objednávky).</param>
    public Cart(string customerId, DateTime createdAtUtc)
    {
        if (string.IsNullOrWhiteSpace(customerId))
        {
            throw new DomainException("Košík musí patřit zákazníkovi.");
        }

        CustomerId = customerId;
        UpdatedAtUtc = createdAtUtc;
        CheckoutId = Guid.NewGuid();
    }

    public int Id { get; private set; }

    public string CustomerId { get; private set; } = string.Empty;

    /// <summary>Kdy se košík naposledy změnil (e-shop krok 10: od té doby se počítá „ticho“ opuštěného košíku).</summary>
    public DateTime UpdatedAtUtc { get; private set; }

    /// <summary>
    /// E-shop krok 10: kdy odešla připomínka opuštěného košíku. Vynuluje se, až se košík vyprázdní (objednávka,
    /// vyprázdnění) – nejvýš jedna připomínka na jeden nákup.
    /// </summary>
    public DateTime? ReminderSentAtUtc { get; private set; }

    public IReadOnlyCollection<CartLine> Lines => _lines.AsReadOnly();

    /// <summary>
    /// E-shop krok 4: „verze“ obsahu košíku pro pokladnu. Mění se při každé změně košíku i po objednání.
    /// Formulář pokladny ji pošle zpět: stejná verze = zákazník objednává přesně to, co viděl; po objednání
    /// stejné ID najde hotovou objednávku (idempotence – dvojklik, F5). Hlídá i souběh (concurrency token).
    /// </summary>
    public Guid CheckoutId { get; private set; }

    /// <summary>
    /// E-shop krok 13: slevový kód, který si zákazník v pokladně uplatnil (normalizovaný), nebo null. Platnost se ověřuje
    /// při uplatnění, v souhrnu pokladny a znovu při objednání – kupon se mezitím mohl vyčerpat.
    /// </summary>
    public string? CouponCode { get; private set; }

    /// <summary>Počet kusů v košíku (číslo v hlavičce webu).</summary>
    public int TotalQuantity => _lines.Sum(l => l.Quantity);

    /// <summary>E-shop krok 17: verze proudu událostí – číslo poslední uložené události (0 = žádná).</summary>
    public int Version { get; private set; }

    /// <summary>E-shop krok 17: události vyvolané od načtení, které se při uložení přidají do proudu.</summary>
    public IReadOnlyList<CartEvent> UncommittedEvents => _uncommittedEvents.AsReadOnly();

    /// <summary>
    /// Přidá produkt z kopie katalogu. Už v košíku je → navýší množství a zapamatuje si aktuální cenu
    /// (zákazník ji právě viděl ve výloze).
    /// </summary>
    public void AddItem(CatalogProduct product, int quantity, DateTime nowUtc)
    {
        if (product.IsRemoved)
        {
            throw new DomainException($"Produkt '{product.Name}' už není v nabídce.");
        }

        if (product.Availability == CatalogAvailability.OutOfStock)
        {
            throw new DomainException($"Produkt '{product.Name}' je vyprodaný.");
        }

        if (quantity <= 0)
        {
            throw new DomainException("Množství musí být větší než nula.");
        }

        var line = FindLine(product.Id);
        if (line is null && _lines.Count >= MaxLines)
        {
            throw new DomainException($"Košík může obsahovat nejvýš {MaxLines} různých produktů.");
        }

        EnsureQuantity((line?.Quantity ?? 0) + quantity);
        Raise(new CartItemAdded(nowUtc, Guid.NewGuid(), product.Id, quantity, product.Price));
    }

    public void ChangeQuantity(int productId, int quantity, DateTime nowUtc)
    {
        _ = FindLine(productId) ?? throw new DomainException("Produkt v košíku není.");
        EnsureQuantity(quantity);
        Raise(new CartItemQuantityChanged(nowUtc, Guid.NewGuid(), productId, quantity));
    }

    /// <summary>Odebere řádek. Když v košíku není (odebraný v jiné záložce), nic se nestane.</summary>
    public void RemoveItem(int productId, DateTime nowUtc)
    {
        if (FindLine(productId) is null)
        {
            return;
        }

        Raise(new CartItemRemoved(nowUtc, Guid.NewGuid(), productId));
    }

    /// <summary>Vyprázdní košík.</summary>
    public void Clear(DateTime nowUtc) => Raise(new CartCleared(nowUtc, Guid.NewGuid()));

    /// <summary>
    /// E-shop krok 4: zákazník viděl nové ceny a souhlasí s nimi – zapamatují se jako ceny „při přidání“,
    /// upozornění zmizí a pokladna je pustí.
    /// </summary>
    public void AcceptCurrentPrices(IReadOnlyDictionary<int, CatalogProduct> catalog, DateTime nowUtc)
    {
        var prices = _lines
            .Where(line => catalog.ContainsKey(line.ProductId))
            .Select(line => new CartEventLine(line.ProductId, line.Quantity, catalog[line.ProductId].Price))
            .ToList();
        Raise(new CartPricesAccepted(nowUtc, Guid.NewGuid(), prices));
    }

    /// <summary>
    /// E-shop krok 4: pokladna – ověří, že zákazník objednává přesně to, co viděl, a vyprázdní košík.
    /// Vrací řádky pro objednávku; objednávku zakládá aplikační vrstva ve stejné transakci.
    /// </summary>
    /// <param name="checkoutId">Verze košíku z formuláře pokladny (<see cref="CheckoutId"/>).</param>
    /// <param name="catalog">Produkty košíku z kopie katalogu (i stažené).</param>
    public IReadOnlyList<(CatalogProduct Product, int Quantity)> CheckOut(
        Guid checkoutId, IReadOnlyDictionary<int, CatalogProduct> catalog, DateTime nowUtc)
    {
        if (checkoutId != CheckoutId)
        {
            throw new DomainException("Košík se mezitím změnil. Zkontrolujte ho a objednejte znovu.");
        }

        if (_lines.Count == 0)
        {
            throw new DomainException("Košík je prázdný.");
        }

        var result = new List<(CatalogProduct, int)>();
        foreach (var line in OrderedLines)
        {
            if (!catalog.TryGetValue(line.ProductId, out var product) || product.IsRemoved)
            {
                throw new DomainException("Košík obsahuje produkt, který už není v nabídce. Odeberte ho z košíku.");
            }

            if (product.Availability == CatalogAvailability.OutOfStock)
            {
                throw new DomainException($"Produkt '{product.Name}' je vyprodaný. Odeberte ho z košíku.");
            }

            // Žádné tiché zdražení (ani zlevnění): změnu ceny musí zákazník vidět a potvrdit.
            if (product.Price != line.PriceWhenAdded)
            {
                throw new DomainException("Ceny některých produktů se změnily. Zkontrolujte je a potvrďte nové ceny.");
            }

            result.Add((product, line.Quantity));
        }

        Raise(new CartCheckedOut(nowUtc, Guid.NewGuid(),
            OrderedLines.Select(line => new CartEventLine(line.ProductId, line.Quantity, line.PriceWhenAdded)).ToList()));
        return result;
    }

    /// <summary>
    /// E-shop krok 10: košík je opuštěný – něco v něm je, <paramref name="after"/> se nezměnil a připomínka ještě nešla.
    /// Reagujeme na „ticho“: ne na událost, ale na to, že žádná (změna, objednávka) nepřišla.
    /// </summary>
    public bool IsAbandoned(DateTime nowUtc, TimeSpan after) =>
        _lines.Count > 0 && ReminderSentAtUtc is null && nowUtc - UpdatedAtUtc >= after;

    /// <summary>
    /// E-shop krok 10: zapamatovat si, že připomínka odešla. Obsah ani verze pro pokladnu se nemění – otevřená
    /// pokladna dál platí, a „ticho“ se nepočítá znovu od začátku.
    /// </summary>
    public void MarkReminderSent(DateTime nowUtc, TimeSpan after)
    {
        if (!IsAbandoned(nowUtc, after))
        {
            throw new DomainException("Košík není opuštěný nebo připomínka už odešla.");
        }

        Raise(new CartReminderSent(nowUtc));
    }

    /// <summary>E-shop krok 13: zákazník uplatnil slevový kód – mění se cena, proto i verze pro pokladnu.</summary>
    public void ApplyCoupon(string code, DateTime nowUtc)
    {
        if (_lines.Count == 0)
        {
            throw new DomainException("Slevový kód jde uplatnit jen na košík se zbožím.");
        }

        Raise(new CartCouponApplied(nowUtc, Guid.NewGuid(), Coupons.Coupon.NormalizeCode(code)));
    }

    public void RemoveCoupon(DateTime nowUtc)
    {
        if (CouponCode is null)
        {
            return;
        }

        Raise(new CartCouponRemoved(nowUtc, Guid.NewGuid()));
    }

    // ---------- E-shop krok 17: event sourcing ----------

    /// <summary>Událost, po které stav nezávisí na dřívějších událostech – načtení košíku stačí začít od ní.</summary>
    public static bool IsReset(CartEvent cartEvent) => cartEvent is CartCheckedOut or CartCleared or CartSnapshotTaken;

    /// <summary>Část proudu, která stačí k sestavení stavu – od poslední „nuly“ (<see cref="IsReset"/>) do konce.</summary>
    public static IReadOnlyList<VersionedCartEvent> FromLastReset(IReadOnlyList<VersionedCartEvent> stream)
    {
        var lastReset = stream.Select((e, index) => (e, index)).LastOrDefault(x => IsReset(x.e.Event), (default!, 0)).index;
        return stream.Skip(lastReset).ToList();
    }

    /// <summary>Košík jen z událostí (testy, historie, statistiky) – bez projekce.</summary>
    public static Cart FromHistory(string customerId, IEnumerable<VersionedCartEvent> history)
    {
        var cart = new Cart { CustomerId = customerId };
        foreach (var (version, cartEvent) in history)
        {
            cart.Apply(cartEvent);
            cart.Version = version;
        }

        return cart;
    }

    /// <summary>
    /// Načtený košík (projekce z tabulek) si stav sestaví z událostí – od poslední „nuly“ (<see cref="IsReset"/>).
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item>Žádné události (košík z doby před krokem 17) → snapshot z projekce (<see cref="CartSnapshotTaken.ImportedReason"/>).</item>
    /// <item>Verze pro pokladnu nesedí – projekci změnila starší verze kódu (postupné nasazení) → snapshot
    /// (<see cref="CartSnapshotTaken.ResyncedReason"/>); změna zákazníka se tím neztratí.</item>
    /// <item>Jinak platí události: stav z nich se zapíše do projekce (měl by být stejný).</item>
    /// </list>
    /// Snapshot je nezapsaná událost – uloží se s nejbližší změnou košíku.
    /// </remarks>
    public void Rehydrate(IReadOnlyList<VersionedCartEvent> historyFromLastReset)
    {
        Version = historyFromLastReset.Count == 0 ? 0 : historyFromLastReset[^1].Version;
        var replayed = FromHistory(CustomerId, historyFromLastReset);

        if (historyFromLastReset.Count == 0 || replayed.CheckoutId != CheckoutId)
        {
            var reason = historyFromLastReset.Count == 0 ? CartSnapshotTaken.ImportedReason : CartSnapshotTaken.ResyncedReason;
            Raise(new CartSnapshotTaken(UpdatedAtUtc, CheckoutId,
                OrderedLines.Select(line => new CartEventLine(line.ProductId, line.Quantity, line.PriceWhenAdded)).ToList(),
                CouponCode, ReminderSentAtUtc, reason));
            return;
        }

        // Pravdu mají události: projekci srovnat podle nich (řádky podle produktu – EF pozná, co se změnilo).
        UpdatedAtUtc = replayed.UpdatedAtUtc;
        ReminderSentAtUtc = replayed.ReminderSentAtUtc;
        CouponCode = replayed.CouponCode;
        SetLines(replayed._lines.Select(line => new CartEventLine(line.ProductId, line.Quantity, line.PriceWhenAdded)));
    }

    /// <summary>Události jsou uložené (volá infrastruktura po úspěšném uložení).</summary>
    public void MarkEventsCommitted()
    {
        Version += _uncommittedEvents.Count;
        _uncommittedEvents.Clear();
    }

    /// <summary>Zapsat událost: nejdřív změnit stav (když pravidlo selže, nic se nezapíše), pak si ji zapamatovat.</summary>
    private void Raise(CartEvent cartEvent)
    {
        Apply(cartEvent);
        _uncommittedEvents.Add(cartEvent);
    }

    /// <summary>
    /// Jediné místo, kde se mění stav košíku – při nové události i při přehrávání historie. Pravidla tady už neplatí:
    /// událost se stala, jen se promítne (pravidla hlídají metody před <see cref="Raise"/>).
    /// </summary>
    private void Apply(CartEvent cartEvent)
    {
        switch (cartEvent)
        {
            case CartItemAdded e:
                if (FindLine(e.ProductId) is { } line)
                {
                    line.Restore(line.Quantity + e.Quantity, e.Price);
                }
                else
                {
                    _lines.Add(new CartLine(e.ProductId, e.Quantity, e.Price));
                }

                break;
            case CartItemQuantityChanged e:
                if (FindLine(e.ProductId) is { } changed)
                {
                    changed.Restore(e.Quantity, changed.PriceWhenAdded);
                }

                break;
            case CartItemRemoved e:
                _lines.RemoveAll(l => l.ProductId == e.ProductId);
                break;
            case CartPricesAccepted e:
                foreach (var price in e.Prices)
                {
                    FindLine(price.ProductId)?.AcceptPrice(price.Price);
                }

                break;
            case CartCouponApplied e:
                CouponCode = e.CouponCode;
                break;
            case CartCouponRemoved:
                CouponCode = null;
                break;
            case CartCheckedOut or CartCleared:
                _lines.Clear();
                break;
            case CartReminderSent e:
                ReminderSentAtUtc = e.OccurredAtUtc;
                return;   // obsah ani verze pro pokladnu se nemění, „ticho“ se nepočítá znovu
            case CartSnapshotTaken e:
                SetLines(e.Lines);
                CouponCode = e.CouponCode;
                ReminderSentAtUtc = e.ReminderSentAtUtc;
                UpdatedAtUtc = e.OccurredAtUtc;
                CheckoutId = e.CheckoutId;
                return;   // snapshot přebírá stav tak, jak byl – nic nenuluje
            default:
                throw new InvalidOperationException($"Neznámá událost košíku {cartEvent.GetType().Name}.");
        }

        var content = (CartContentEvent)cartEvent;
        Changed(content.OccurredAtUtc, content.CheckoutId);
    }

    /// <summary>Každá změna obsahu = nová verze pro pokladnu (starý formulář pokladny už neprojde).</summary>
    private void Changed(DateTime nowUtc, Guid checkoutId)
    {
        UpdatedAtUtc = nowUtc;
        CheckoutId = checkoutId;
        if (_lines.Count == 0)
        {
            ReminderSentAtUtc = null;   // nákup skončil (objednávka / vyprázdnění) – příští košík smí dostat připomínku
            CouponCode = null;          // e-shop krok 13: kód patří k nákupu, příští košík začíná bez něj
        }
    }

    /// <summary>Řádky podle produktu: existující upravit, chybějící přidat, přebývající odebrat; pořadí jako v <paramref name="lines"/>.</summary>
    private void SetLines(IEnumerable<CartEventLine> lines)
    {
        var wanted = lines.ToList();
        _lines.RemoveAll(line => wanted.All(w => w.ProductId != line.ProductId));
        foreach (var w in wanted)
        {
            if (FindLine(w.ProductId) is { } line)
            {
                line.Restore(w.Quantity, w.Price);
            }
            else
            {
                _lines.Add(new CartLine(w.ProductId, w.Quantity, w.Price));
            }
        }

        var order = wanted.Select(w => w.ProductId).ToList();
        _lines.Sort((a, b) => order.IndexOf(a.ProductId).CompareTo(order.IndexOf(b.ProductId)));
    }

    /// <summary>Řádky v pořadí přidání: uložené podle ID, nové (ještě bez ID) za nimi v pořadí seznamu.</summary>
    private IEnumerable<CartLine> OrderedLines =>
        _lines.Select((line, index) => (line, index))
            .OrderBy(x => x.line.Id == 0 ? int.MaxValue : x.line.Id)
            .ThenBy(x => x.index)
            .Select(x => x.line);

    private static void EnsureQuantity(int quantity)
    {
        if (quantity is < 1 or > MaxQuantity)
        {
            throw new DomainException($"Množství musí být od 1 do {MaxQuantity} kusů.");
        }
    }

    private CartLine? FindLine(int productId) => _lines.FirstOrDefault(l => l.ProductId == productId);
}
