using MiniShop.Domain.Exceptions;

namespace MiniShop.Domain.Invoicing;

/// <summary>
/// E-shop krok 15: faktura – účetní doklad. Jednou vystavená se nemění (žádná metoda, která by ji upravila);
/// oprava = nový doklad (dobropis, e-shop krok 15B). Nese vlastní kopii všech údajů z doby vystavení.
/// </summary>
/// <remarks>
/// Jedna faktura na objednávku (unikátní <see cref="OrderId"/>) – opakovaná zpráva druhou nevystaví.
/// Číslo přiděluje <see cref="InvoiceNumberSeries"/> ve stejné transakci, takže řada nemá díry.
/// </remarks>
public sealed class Invoice
{
    private readonly List<InvoiceLine> _lines = new();

    // Konstruktor pro EF Core.
    private Invoice()
    {
    }

    public int Id { get; private set; }

    /// <summary>Číslo z nepřerušené řady roku – „2026000001“.</summary>
    public string Number { get; private set; } = string.Empty;

    public int OrderId { get; private set; }

    /// <summary>Komu patří (stažení PDF jen vlastník nebo admin). Faktura zůstane i po smazání účtu – zákonná povinnost.</summary>
    public string CustomerId { get; private set; } = string.Empty;

    public string CustomerName { get; private set; } = string.Empty;

    public string? DeliveryAddress { get; private set; }

    public string ShippingMethod { get; private set; } = string.Empty;

    public decimal ShippingPrice { get; private set; }

    public decimal Total { get; private set; }

    public DateTime IssuedAtUtc { get; private set; }

    /// <summary>Datum úhrady (u neplátce DPH zároveň datum uskutečnění plnění).</summary>
    public DateTime PaidAtUtc { get; private set; }

    public string PaymentNote { get; private set; } = string.Empty;

    public IReadOnlyCollection<InvoiceLine> Lines => _lines.AsReadOnly();

    /// <summary>Vystaví fakturu ze zaplacené objednávky s číslem z řady.</summary>
    public static Invoice Issue(BillableOrder order, string number, DateTime issuedAtUtc)
    {
        if (!order.IsReadyToInvoice)
        {
            throw new DomainException($"Objednávku {order.OrderId} zatím nejde fakturovat – chybí údaje nebo platba.");
        }

        var invoice = new Invoice
        {
            Number = number,
            OrderId = order.OrderId,
            CustomerId = order.CustomerId!,
            CustomerName = order.CustomerName ?? string.Empty,
            DeliveryAddress = order.DeliveryAddress,
            ShippingMethod = order.ShippingMethod ?? string.Empty,
            ShippingPrice = order.ShippingPrice,
            Total = order.Total,
            IssuedAtUtc = issuedAtUtc,
            PaidAtUtc = order.PaidAtUtc!.Value,
            PaymentNote = order.PaymentNote ?? string.Empty
        };
        invoice._lines.AddRange(order.Lines);
        return invoice;
    }
}
