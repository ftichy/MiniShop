using MiniShop.Application.Abstractions;

namespace MiniShop.Application.Notifications;

/// <summary>
/// E-shop krok 15: Notifications potřebují PDF faktury do přílohy, ale modul Invoicing neznají (stejně jako
/// <see cref="ICustomerContacts"/> neznají Accounts). Rozhraní patří sem, implementaci dodá Web, kde běží oba moduly.
/// PDF tak nejde přes RabbitMQ – ve zprávě <c>InvoiceIssuedV1</c> je jen číslo.
/// </summary>
public interface IInvoiceDocuments
{
    /// <summary>PDF faktury k objednávce jako příloha, nebo null (faktura neexistuje).</summary>
    Task<EmailAttachment?> GetInvoiceAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>E-shop krok 15B: PDF dobropisu k objednávce jako příloha, nebo null (dobropis neexistuje).</summary>
    Task<EmailAttachment?> GetCreditNoteAsync(int orderId, CancellationToken cancellationToken = default);
}
