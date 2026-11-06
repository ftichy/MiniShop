using MiniShop.Application.Abstractions;
using MiniShop.Domain.Invoicing;

namespace MiniShop.Application.Invoicing;

/// <summary>E-shop krok 15: uložení změn modulu Invoicing – kopie objednávek, faktury, číselné řady, inbox a outbox.</summary>
public interface IInvoicingUnitOfWork : IUnitOfWork;

/// <summary>E-shop krok 15: zprávy z Invoicingu (<c>InvoiceIssuedV1</c>) jdou do outboxu ve schématu invoicing.</summary>
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

/// <summary>E-shop krok 15: PDF faktury (implementace QuestPDF v Infrastructure – aplikační vrstva knihovnu nezná).</summary>
public interface IInvoicePdfRenderer
{
    byte[] Render(Invoice invoice, InvoiceSeller seller);
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

/// <summary>Faktura v přehledu.</summary>
public sealed record InvoiceSummaryDto(string Number, int OrderId, string CustomerName, decimal Total, DateTime IssuedAtUtc);

/// <summary>PDF faktury ke stažení nebo do přílohy e-mailu.</summary>
public sealed record InvoicePdf(string FileName, byte[] Content)
{
    public const string ContentType = "application/pdf";
}
