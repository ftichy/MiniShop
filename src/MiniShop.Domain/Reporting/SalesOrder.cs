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

    /// <summary>Do tržby patří objednávky, o kterých známe detaily a které nejsou zamítnuté ani zrušené.</summary>
    public bool CountsAsRevenue => PlacedAtUtc is not null && Status is not (SalesOrderStatus.Rejected or SalesOrderStatus.Cancelled);

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
        if (IsFinal(Status) || Rank(status) <= Rank(Status))
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

    private static bool IsFinal(SalesOrderStatus status) =>
        status is SalesOrderStatus.Completed or SalesOrderStatus.Rejected or SalesOrderStatus.Cancelled;

    private static int Rank(SalesOrderStatus status) => status switch
    {
        SalesOrderStatus.Placed => 0,
        SalesOrderStatus.Confirmed => 1,
        _ => 2   // konečné stavy
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

    Cancelled
}

/// <summary>Řádek objednávky v přehledu – název a cena z doby objednání.</summary>
public sealed record SalesOrderLine(int ProductId, string ProductName, int Quantity, decimal LineTotal);
