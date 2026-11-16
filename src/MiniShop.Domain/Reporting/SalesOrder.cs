namespace MiniShop.Domain.Reporting;

/// <summary>
/// E-shop krok 11 (CQRS): jedna objednávka v čtecím modelu přehledu tržeb – jen to, na co se admin ptá (kdy, kolik,
/// co, v jakém stavu). Plní ji projekce z integračních událostí; Ordering o ní neví.
/// </summary>
/// <remarks>
/// <para>
/// Zprávy chodí mimo pořadí: potvrzení může předběhnout vznik. Projekce proto řádek založí hned
/// (<see cref="Create"/>, zatím bez detailů) a <see cref="ApplyPlaced"/> ho doplní, až vznik dorazí.
/// </para>
/// <para>
/// Stav nikdy necouvne (<see cref="ApplyStatus"/>): pozdní „potvrzeno“ nepřepíše „zrušeno“. Díky tomu nezáleží
/// na pořadí ani na tom, jestli dorazí událost, nebo snímek z přestavění.
/// </para>
/// Bez osobních údajů – proto přehled nemusí nic mazat, když zákazník smaže účet.
/// </remarks>
public sealed class SalesOrder
{
    private readonly List<SalesOrderLine> _lines = new();
    private readonly List<SalesOrderReturn> _returns = new();
    private readonly List<SalesOrderReturnLine> _returnedLines = new();

    // Konstruktor pro EF Core.
    private SalesOrder()
    {
    }

    /// <summary>Číslo objednávky z Orderingu (ne vlastní ID).</summary>
    public int OrderId { get; private set; }

    /// <summary>Kdy objednávka vznikla; null = vznik (OrderPlacedV2) ještě nedorazil.</summary>
    public DateTime? PlacedAtUtc { get; private set; }

    public SalesOrderStatus Status { get; private set; }

    public decimal TotalPrice { get; private set; }

    public decimal ShippingPrice { get; private set; }

    /// <summary>Kolik peněz se vrátilo (PaymentRefundedV1) – jen u zamítnutých a zrušených, do tržby se nepočítá.</summary>
    public decimal RefundedAmount { get; private set; }

    /// <summary>Kdy projekce řádek naposledy změnila („data k…“ na stránce – přehled je o chvilku pozadu).</summary>
    public DateTime UpdatedAtUtc { get; private set; }

    public IReadOnlyCollection<SalesOrderLine> Lines => _lines.AsReadOnly();

    /// <summary>E-shop krok 22: vratky po částech (objednávka zůstává doručená, z tržby se odečte jen vrácené).</summary>
    public IReadOnlyCollection<SalesOrderReturn> Returns => _returns.AsReadOnly();

    /// <summary>E-shop krok 22: vrácené kusy – odečtou se z „top produktů“.</summary>
    public IReadOnlyCollection<SalesOrderReturnLine> ReturnedLines => _returnedLines.AsReadOnly();

    /// <summary>E-shop krok 22: kolik vrátily vratky po částech celkem.</summary>
    public decimal ReturnedAmount => _returns.Sum(r => r.Amount);

    /// <summary>Tržba z objednávky – cena (včetně dopravy) bez vratek po částech.</summary>
    public decimal Revenue => TotalPrice - ReturnedAmount;

    /// <summary>
    /// E-shop krok 22: řádky, které si zákazník nechal – objednané kusy a částky bez vrácených (pro „top produkty“).
    /// Vrácené se odečítají od řádků téhož produktu postupně; řádek vrácený celý zmizí.
    /// </summary>
    public IReadOnlyList<SalesOrderLine> KeptLines()
    {
        var returned = _returnedLines.GroupBy(l => l.ProductId)
            .ToDictionary(g => g.Key, g => (Quantity: g.Sum(l => l.Quantity), Total: g.Sum(l => l.LineTotal)));
        var kept = new List<SalesOrderLine>();
        foreach (var line in _lines)
        {
            var (quantity, total) = returned.GetValueOrDefault(line.ProductId);
            var minusQuantity = Math.Min(quantity, line.Quantity);
            var minusTotal = Math.Min(total, line.LineTotal);
            returned[line.ProductId] = (quantity - minusQuantity, total - minusTotal);
            if (line.Quantity - minusQuantity > 0)
            {
                kept.Add(line with { Quantity = line.Quantity - minusQuantity, LineTotal = line.LineTotal - minusTotal });
            }
        }

        return kept;
    }

    /// <summary>Do tržby patří objednávky, o kterých známe detaily a které nejsou zamítnuté ani zrušené.</summary>
    public bool CountsAsRevenue => PlacedAtUtc is not null
        && Status is not (SalesOrderStatus.Rejected or SalesOrderStatus.Cancelled or SalesOrderStatus.Returned);

    public static SalesOrder Create(int orderId, DateTime nowUtc) =>
        new() { OrderId = orderId, Status = SalesOrderStatus.Placed, UpdatedAtUtc = nowUtc };

    /// <summary>Detaily objednávky (ze vzniku nebo ze snímku). Podruhé je přepíše – snímek je čerstvější.</summary>
    public void ApplyPlaced(DateTime placedAtUtc, decimal totalPrice, decimal shippingPrice,
        IEnumerable<SalesOrderLine> lines, DateTime nowUtc)
    {
        PlacedAtUtc = placedAtUtc;
        TotalPrice = totalPrice;
        ShippingPrice = shippingPrice;
        _lines.Clear();
        _lines.AddRange(lines);
        UpdatedAtUtc = nowUtc;
    }

    /// <summary>
    /// Posune stav dopředu. Konečný stav (doručeno, zamítnuto, zrušeno) se už nezmění; Placed → Confirmed → Completed
    /// jen dopředu. Vrací false, když zpráva stav nezměnila (pozdní nebo duplicitní).
    /// </summary>
    public bool ApplyStatus(SalesOrderStatus status, DateTime nowUtc)
    {
        // E-shop krok 12: z doručené jde už jen „vrácená“ (vratka) – jiný pozdní stav ji nepřepíše.
        var allowed = Status == SalesOrderStatus.Completed
            ? status == SalesOrderStatus.Returned
            : !IsFinal(Status) && Rank(status) > Rank(Status);
        if (!allowed)
        {
            return false;
        }

        Status = status;
        UpdatedAtUtc = nowUtc;
        return true;
    }

    public void AddRefund(decimal amount, DateTime nowUtc)
    {
        RefundedAmount += amount;
        UpdatedAtUtc = nowUtc;
    }

    /// <summary>
    /// E-shop krok 22: vratka po částech (ReturnCompletedV2). Stav se nemění – objednávka zůstává doručená a do tržby
    /// se počítá bez vrácené částky. Každá vratka jednou (podle čísla). Vrací false, když už ji přehled zná.
    /// </summary>
    public bool ApplyReturn(int returnNumber, decimal amount, IEnumerable<SalesOrderReturnLine> lines, DateTime nowUtc)
    {
        if (_returns.Any(r => r.ReturnNumber == returnNumber))
        {
            return false;
        }

        _returns.Add(new SalesOrderReturn(returnNumber, amount));
        _returnedLines.AddRange(lines.Where(l => l.ReturnNumber == returnNumber));
        UpdatedAtUtc = nowUtc;
        return true;
    }

    private static bool IsFinal(SalesOrderStatus status) =>
        status is SalesOrderStatus.Rejected or SalesOrderStatus.Cancelled or SalesOrderStatus.Returned;

    private static int Rank(SalesOrderStatus status) => status switch
    {
        SalesOrderStatus.Placed => 0,
        SalesOrderStatus.Confirmed => 1,
        SalesOrderStatus.Completed => 2,
        _ => 3   // konečné stavy
    };
}

/// <summary>Zjednodušený stav pro přehled – jemné kroky Orderingu (čeká na platbu, balí se…) admina tady nezajímají.</summary>
/// <remarks>Ukládá se jako text – nové hodnoty jen na konec.</remarks>
public enum SalesOrderStatus
{
    /// <summary>Přijatá, čeká na sklad nebo platbu.</summary>
    Placed,

    /// <summary>Potvrzená (i balená nebo odeslaná).</summary>
    Confirmed,

    /// <summary>Doručená / vyzvednutá.</summary>
    Completed,

    Rejected,

    Cancelled,

    /// <summary>E-shop krok 12: vrácená (zboží zpět, peníze vrácené) – do tržby se nepočítá.</summary>
    Returned
}

/// <summary>Řádek objednávky v přehledu – název a cena z doby objednání.</summary>
public sealed record SalesOrderLine(int ProductId, string ProductName, int Quantity, decimal LineTotal);

/// <summary>E-shop krok 22: vratka po částech v přehledu – číslo vratky a vrácená částka (položky + doprava).</summary>
public sealed record SalesOrderReturn(int ReturnNumber, decimal Amount);

/// <summary>E-shop krok 22: vrácené kusy produktu v jedné vratce.</summary>
public sealed record SalesOrderReturnLine(int ReturnNumber, int ProductId, int Quantity, decimal LineTotal);
