using MiniShop.Application.Abstractions;
using MiniShop.Application.Invoicing;
using MiniShop.Application.Notifications;

namespace MiniShop.Web.Invoicing;

/// <summary>
/// E-shop krok 15: most mezi moduly – Notifications chtějí PDF faktury do přílohy, Invoicing ho umí vyrobit.
/// Moduly se neznají; spojí je až Web, kde oba běží (jako <c>IdentityCustomerContacts</c> u připomínky košíku).
/// </summary>
public sealed class InvoiceDocuments(InvoiceService invoices) : IInvoiceDocuments
{
    public async Task<EmailAttachment?> GetInvoiceAsync(int orderId, CancellationToken cancellationToken = default)
    {
        // customerId null = bez kontroly vlastníka – posílá se jen na e-mail z objednávky.
        var pdf = await invoices.GetPdfAsync(orderId, customerId: null, cancellationToken);
        return pdf is null ? null : new EmailAttachment(pdf.FileName, InvoicePdf.ContentType, pdf.Content);
    }
}
