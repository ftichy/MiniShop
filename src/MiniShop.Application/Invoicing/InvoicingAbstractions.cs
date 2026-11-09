using MiniShop.Application.Abstractions;
using MiniShop.Domain.Invoicing;

namespace MiniShop.Application.Invoicing;

/// <summary>E-shop krok 15: uložení změn modulu Invoicing – kopie objednávek, faktury, číselné řady, inbox a outbox.</summary>
public interface IInvoicingUnitOfWork : IUnitOfWork;

/// <summary>E-shop krok 15: zprávy z Invoicingu (<c>InvoiceIssuedV1</c>, <c>CreditNoteIssuedV1</c>) jdou do outboxu ve schématu invoicing.</summary>
public interface IInvoicingMessagePublisher : IIntegrationMessagePublisher;

/// <summary>E-shop krok 15: kopie objednávek (podklad pro fakturu).</summary>
public interface IBillableOrderRepository
{
    Task<BillableOrder?> GetAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>Zapomenout zákazníka (smazal účet) – kopie objednávek pryč, vystavené faktury zůstávají (zákon).</summary>
    Task RemoveForCustomerAsync(string customerId, CancellationToken cancellationToken = default);

    void Add(BillableOrder order);
}

/// <summary>E-shop krok 15: vystavené faktury.</summary>
public interface IInvoiceRepository
{
    Task<Invoice?> GetByOrderAsync(int orderId, CancellationToken cancellationToken = default);

    Task<bool> ExistsForOrderAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>Nejnovější faktury (přehled pro admina).</summary>
    Task<IReadOnlyList<Invoice>> GetLatestAsync(int count, CancellationToken cancellationToken = default);

    void Add(Invoice invoice);
}

/// <summary>E-shop krok 15: číselné řady faktur (řádek na rok).</summary>
public interface IInvoiceNumberSeriesRepository
{
    Task<InvoiceNumberSeries?> GetAsync(int year, CancellationToken cancellationToken = default);

    void Add(InvoiceNumberSeries series);
}

/// <summary>E-shop krok 15B: vystavené dobropisy (nejvýš jeden na objednávku).</summary>
public interface ICreditNoteRepository
{
    Task<CreditNote?> GetByOrderAsync(int orderId, CancellationToken cancellationToken = default);

    Task<bool> ExistsForOrderAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>Dobropisy k objednávkám (přehled faktur pro admina) – klíč = číslo objednávky.</summary>
    Task<IReadOnlyDictionary<int, CreditNote>> GetByOrdersAsync(IReadOnlyCollection<int> orderIds, CancellationToken cancellationToken = default);

    void Add(CreditNote creditNote);
}

/// <summary>E-shop krok 15B: číselné řady dobropisů (řádek na rok).</summary>
public interface ICreditNoteNumberSeriesRepository
{
    Task<CreditNoteNumberSeries?> GetAsync(int year, CancellationToken cancellationToken = default);

    void Add(CreditNoteNumberSeries series);
}

/// <summary>E-shop krok 15: PDF faktury a dobropisu (implementace QuestPDF v Infrastructure – aplikační vrstva knihovnu nezná).</summary>
public interface IInvoicePdfRenderer
{
    byte[] Render(Invoice invoice, InvoiceSeller seller);

    /// <summary>E-shop krok 15B: dobropis – odkaz na fakturu, důvod, částky se znaménkem minus.</summary>
    byte[] Render(CreditNote creditNote, InvoiceSeller seller);
}

/// <summary>
/// E-shop krok 15: prodávající na faktuře (konfigurace <c>Invoicing:Seller</c>). MiniShop je výukový projekt – údaje
/// jsou fiktivní a prodávající je neplátce DPH (faktura bez rozpisu daně).
/// </summary>
public sealed record InvoiceSeller(string Name, string Address, string? CompanyId, string Note)
{
    public static InvoiceSeller Default { get; } = new(
        "MiniShop – výukový projekt",
        "Ukázková 1, 110 00 Praha",
        null,
        "Nejsem plátce DPH. Výukový projekt – doklad nemá účetní platnost.");
}

/// <summary>Faktura v přehledu; <paramref name="CreditNoteNumber"/> = dobropis k ní (e-shop krok 15B), null = žádný.</summary>
public sealed record InvoiceSummaryDto(
    string Number,
    int OrderId,
    string CustomerName,
    decimal Total,
    DateTime IssuedAtUtc,
    string? CreditNoteNumber = null);

/// <summary>Doklady k objednávce (detail objednávky): číslo faktury a dobropisu, null = zatím není.</summary>
public sealed record OrderDocumentsDto(string? InvoiceNumber, string? CreditNoteNumber);

/// <summary>PDF faktury ke stažení nebo do přílohy e-mailu.</summary>
public sealed record InvoicePdf(string FileName, byte[] Content)
{
    public const string ContentType = "application/pdf";
}
