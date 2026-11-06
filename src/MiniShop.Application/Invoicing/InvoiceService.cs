namespace MiniShop.Application.Invoicing;

/// <summary>
/// E-shop krok 15: čtení faktur – PDF ke stažení (zákazník jen svou, admin jakoukoli) a přehled pro admina.
/// Kdo smí co, rozhoduje volající (Web) parametrem <c>customerId</c>: null = admin.
/// </summary>
public sealed class InvoiceService(IInvoiceRepository invoices, IInvoicePdfRenderer renderer, InvoiceSeller seller)
{
    /// <summary>PDF faktury k objednávce, nebo null (faktura zatím není, nebo patří někomu jinému – Web vrátí 404).</summary>
    public async Task<InvoicePdf?> GetPdfAsync(int orderId, string? customerId, CancellationToken cancellationToken = default)
    {
        var invoice = await invoices.GetByOrderAsync(orderId, cancellationToken);
        if (invoice is null || (customerId is not null && invoice.CustomerId != customerId))
        {
            return null;
        }

        // PDF se skládá vždy znovu ze snímku faktury – údaje se nemění, jen vzhled dokumentu.
        return new InvoicePdf($"faktura-{invoice.Number}.pdf", renderer.Render(invoice, seller));
    }

    /// <summary>Číslo faktury k objednávce (odkaz na detailu objednávky), nebo null.</summary>
    public async Task<string?> GetNumberAsync(int orderId, string? customerId, CancellationToken cancellationToken = default)
    {
        var invoice = await invoices.GetByOrderAsync(orderId, cancellationToken);
        return invoice is null || (customerId is not null && invoice.CustomerId != customerId) ? null : invoice.Number;
    }

    public async Task<IReadOnlyList<InvoiceSummaryDto>> GetLatestAsync(int count, CancellationToken cancellationToken = default) =>
        (await invoices.GetLatestAsync(count, cancellationToken))
            .Select(invoice => new InvoiceSummaryDto(invoice.Number, invoice.OrderId, invoice.CustomerName, invoice.Total, invoice.IssuedAtUtc))
            .ToList();
}
