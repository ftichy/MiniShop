namespace MiniShop.Domain.Invoicing;

/// <summary>
/// E-shop krok 15: kopie objednávky v modulu Invoicing – podklad pro fakturu (komu, co, za kolik) a jestli je zaplacená.
/// Plní se z událostí (event-carried state transfer), na Ordering se Invoicing nikdy neptá.
/// </summary>
/// <remarks>
/// Zprávy mimo pořadí: platba může dorazit dřív než vznik objednávky – řádek se založí hned (<see cref="Create"/>)
/// a vznik ho doplní. Zaplaceno se jen zapne, nikdy nevypne. Fakturu jde vystavit, až je známé obojí (<see cref="IsReadyToInvoice"/>).
/// </remarks>
public sealed class BillableOrder
{
    private readonly List<InvoiceLine> _lines = new();

    // Konstruktor pro EF Core.
    private BillableOrder()
    {
    }

    /// <summary>Číslo objednávky z Orderingu (klíč).</summary>
    public int OrderId { get; private set; }

    /// <summary>ID účtu zákazníka; null = vznik objednávky (OrderPlacedV2) ještě nedorazil.</summary>
    public string? CustomerId { get; private set; }

    public string? CustomerName { get; private set; }

    public string? DeliveryAddress { get; private set; }

    public string? ShippingMethod { get; private set; }

    public decimal ShippingPrice { get; private set; }

    public decimal Total { get; private set; }

    public DateTime? PlacedAtUtc { get; private set; }

    /// <summary>Kdy byla objednávka zaplacená (karta: potvrzení brány; dobírka a API: doručení). Null = zatím ne.</summary>
    public DateTime? PaidAtUtc { get; private set; }

    /// <summary>Jak byla zaplacená – text na faktuře („Kartou online“, „Při převzetí“).</summary>
    public string? PaymentNote { get; private set; }

    public IReadOnlyCollection<InvoiceLine> Lines => _lines.AsReadOnly();

    /// <summary>
    /// Objednávka skončila zamítnutím nebo zrušením – fakturovat se nebude (pozdní platbu vrátí sága). Jen se zapne.
    /// Zrušení po vystavení faktury řeší dobropis (e-shop krok 15B, <see cref="CreditReason"/>).
    /// </summary>
    public bool IsVoided { get; private set; }

    /// <summary>
    /// E-shop krok 15B: proč by fakturu měl opravit dobropis (zrušeno, zamítnuto, vráceno); null = nic. Platí první důvod.
    /// Dobropis vznikne, jen když faktura existuje – nebo hned po ní, když vratka dorazila dřív než faktura.
    /// </summary>
    public CreditNoteReason? CreditReason { get; private set; }

    /// <summary>E-shop krok 15B: jak se peníze vrací (text na dobropisu).</summary>
    public string? RefundNote { get; private set; }

    /// <summary>Víme, co a komu fakturovat, je zaplaceno a objednávka neskončila zrušením.</summary>
    public bool IsReadyToInvoice => CustomerId is not null && PaidAtUtc is not null && !IsVoided;

    public static BillableOrder Create(int orderId) => new() { OrderId = orderId };

    /// <summary>Snímek ze vzniku objednávky (OrderPlacedV2) – jména a ceny z doby objednání. Podruhé přepíše.</summary>
    public void ApplyPlaced(
        string customerId,
        string customerName,
        string? deliveryAddress,
        string shippingMethod,
        decimal shippingPrice,
        decimal total,
        DateTime placedAtUtc,
        IEnumerable<InvoiceLine> lines)
    {
        CustomerId = customerId;
        CustomerName = customerName;
        DeliveryAddress = deliveryAddress;
        ShippingMethod = shippingMethod;
        ShippingPrice = shippingPrice;
        Total = total;
        PlacedAtUtc = placedAtUtc;
        _lines.Clear();
        _lines.AddRange(lines);
    }

    /// <summary>Zaplaceno – platí první zpráva (karta dřív než doručení), další nic nemění.</summary>
    public void MarkPaid(DateTime paidAtUtc, string paymentNote)
    {
        if (PaidAtUtc is not null)
        {
            return;
        }

        PaidAtUtc = paidAtUtc;
        PaymentNote = paymentNote;
    }

    /// <summary>
    /// Zamítnuto nebo zrušeno (OrderRejectedV1, OrderCancelledV1). Před vystavením faktury = nefakturovat;
    /// po vystavení (jen karta – dobírka se fakturuje až po doručení) = dobropis, peníze vrací sága na kartu.
    /// </summary>
    public void MarkVoided(CreditNoteReason reason, string refundNote)
    {
        IsVoided = true;
        SetCreditReason(reason, refundNote);
    }

    /// <summary>
    /// E-shop krok 15B: zboží vrácené (ReturnCompletedV1). Fakturovat se dál smí – objednávka byla zaplacená
    /// a doručená – ale k faktuře patří dobropis.
    /// </summary>
    public void MarkReturned(string refundNote) => SetCreditReason(CreditNoteReason.Returned, refundNote);

    private void SetCreditReason(CreditNoteReason reason, string refundNote)
    {
        if (CreditReason is not null)
        {
            return;
        }

        CreditReason = reason;
        RefundNote = refundNote;
    }
}

/// <summary>Řádek faktury – název a ceny z doby objednání (sleva z kuponu je už rozpočítaná do řádku).</summary>
public sealed record InvoiceLine(string ProductName, int Quantity, decimal UnitPrice, decimal Discount, decimal LineTotal);
