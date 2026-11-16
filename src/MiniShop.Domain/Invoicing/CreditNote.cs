using MiniShop.Domain.Exceptions;

namespace MiniShop.Domain.Invoicing;

/// <summary>
/// E-shop krok 15B: dobropis – opravný doklad k faktuře. Faktura se po vystavení nemění; když se zaplacená objednávka
/// zruší nebo vrátí, vznikne nový doklad, který na fakturu odkazuje a celou její částku vrací (faktura + dobropis = 0).
/// </summary>
/// <remarks>
/// Celý dobropis (<see cref="Issue"/>) je k objednávce nejvýš jeden. E-shop krok 22: vratka po částech má vlastní dobropis
/// jen s vrácenými řádky (<see cref="IssueForReturn"/>) – unikátní je dvojice <see cref="OrderId"/> + <see cref="ReturnNumber"/>
/// a součet dobropisů nikdy nepřesáhne fakturu.
/// Skládá se z kopie faktury, ne z kopie objednávky: ta po smazání účtu zmizí, faktura zůstává.
/// Číslo přiděluje <see cref="CreditNoteNumberSeries"/> ve stejné transakci (řada bez děr jako u faktur).
/// </remarks>
public sealed class CreditNote
{
    private readonly List<InvoiceLine> _lines = new();

    // Konstruktor pro EF Core.
    private CreditNote()
    {
    }

    public int Id { get; private set; }

    /// <summary>Číslo z vlastní řady roku – „D2026000001“.</summary>
    public string Number { get; private set; } = string.Empty;

    /// <summary>Číslo opravované faktury (dobropis bez faktury neexistuje).</summary>
    public string InvoiceNumber { get; private set; } = string.Empty;

    public int OrderId { get; private set; }

    /// <summary>
    /// E-shop krok 22: číslo vratky v objednávce (1, 2, …); <see cref="WholeOrder"/> (0) = celý dobropis – zrušení, zamítnutí,
    /// vratka celé objednávky a všechny dobropisy z doby před krokem 22.
    /// </summary>
    public int ReturnNumber { get; private set; }

    /// <summary>Vrací se jen část faktury (vratka po částech).</summary>
    public bool IsPartial => ReturnNumber != WholeOrder;

    /// <summary>Komu patří (stažení PDF jen vlastník nebo admin). Zůstává i po smazání účtu – jako faktura.</summary>
    public string CustomerId { get; private set; } = string.Empty;

    public string CustomerName { get; private set; } = string.Empty;

    public string? DeliveryAddress { get; private set; }

    public string ShippingMethod { get; private set; } = string.Empty;

    public decimal ShippingPrice { get; private set; }

    /// <summary>Kolik se vrací – celá částka faktury, u vratky po částech jen vrácené řádky + doprava (kladné číslo; v PDF se znaménkem minus).</summary>
    public decimal Total { get; private set; }

    public CreditNoteReason Reason { get; private set; }

    /// <summary>Jak se peníze vrací – „Vráceno na kartu“, „Vrátíme převodem na účet“.</summary>
    public string RefundNote { get; private set; } = string.Empty;

    public DateTime IssuedAtUtc { get; private set; }

    /// <summary>Celý dobropis = <see cref="ReturnNumber"/> 0.</summary>
    public const int WholeOrder = 0;

    /// <summary>Řádky opravované faktury (celý dobropis), nebo jen vrácené řádky (vratka po částech).</summary>
    public IReadOnlyCollection<InvoiceLine> Lines => _lines.AsReadOnly();

    /// <summary>Vystaví dobropis k faktuře s číslem z řady dobropisů.</summary>
    public static CreditNote Issue(Invoice invoice, string number, CreditNoteReason reason, string refundNote, DateTime issuedAtUtc)
    {
        if (issuedAtUtc < invoice.IssuedAtUtc)
        {
            throw new DomainException($"Dobropis k faktuře {invoice.Number} nemůže být starší než faktura.");
        }

        var creditNote = new CreditNote
        {
            Number = number,
            InvoiceNumber = invoice.Number,
            OrderId = invoice.OrderId,
            CustomerId = invoice.CustomerId,
            CustomerName = invoice.CustomerName,
            DeliveryAddress = invoice.DeliveryAddress,
            ShippingMethod = invoice.ShippingMethod,
            ShippingPrice = invoice.ShippingPrice,
            Total = invoice.Total,
            Reason = reason,
            RefundNote = refundNote,
            IssuedAtUtc = issuedAtUtc
        };
        creditNote._lines.AddRange(invoice.Lines);
        return creditNote;
    }

    /// <summary>
    /// E-shop krok 22: dobropis k vratce po částech – hlavička z faktury, řádky jen vrácené. Pojistka: s dosavadními dobropisy
    /// (<paramref name="alreadyCredited"/>) nesmí přesáhnout fakturu – hlavní pravidlo „nevrátit víc, než se koupilo“ hlídá Ordering.
    /// </summary>
    public static CreditNote IssueForReturn(
        Invoice invoice,
        string number,
        int returnNumber,
        IReadOnlyCollection<InvoiceLine> lines,
        decimal shippingRefund,
        string refundNote,
        decimal alreadyCredited,
        DateTime issuedAtUtc)
    {
        if (returnNumber <= WholeOrder)
        {
            throw new DomainException($"Číslo vratky musí být kladné (je {returnNumber}).");
        }

        if (lines.Count == 0 || lines.Any(line => line.Quantity <= 0 || line.LineTotal < 0) || shippingRefund < 0)
        {
            throw new DomainException($"Vratka {returnNumber} objednávky {invoice.OrderId} nemá co dobropisovat.");
        }

        var total = lines.Sum(line => line.LineTotal) + shippingRefund;
        if (alreadyCredited + total > invoice.Total)
        {
            throw new DomainException(
                $"Dobropisy k faktuře {invoice.Number} by přesáhly její částku ({alreadyCredited + total} > {invoice.Total}).");
        }

        var creditNote = Issue(invoice, number, CreditNoteReason.Returned, refundNote, issuedAtUtc);
        creditNote.ReturnNumber = returnNumber;
        creditNote.ShippingPrice = shippingRefund;
        creditNote.Total = total;
        creditNote._lines.Clear();
        creditNote._lines.AddRange(lines);
        return creditNote;
    }
}

/// <summary>E-shop krok 15B: proč vznikl dobropis. Ukládá se jako text – nové hodnoty jen na konec.</summary>
public enum CreditNoteReason
{
    /// <summary>Zaplacená objednávka zrušená před odesláním (peníze vrací sága na kartu).</summary>
    Cancelled,

    /// <summary>Zamítnutá objednávka, u které pozdní platba stihla vystavit fakturu (vzácný souběh).</summary>
    Rejected,

    /// <summary>Vrácené zboží (vratka, e-shop krok 12).</summary>
    Returned
}
