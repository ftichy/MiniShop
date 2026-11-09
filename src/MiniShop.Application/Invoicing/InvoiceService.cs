namespace MiniShop.Application.Invoicing;

/// <summary>
/// E-shop krok 15: čtení faktur – PDF ke stažení (zákazník jen svou, admin jakoukoli) a přehled pro admina.
/// E-shop krok 15B: totéž pro dobropisy. Kdo smí co, rozhoduje volající (Web) parametrem <c>customerId</c>: null = admin.
/// </summary>
public sealed class InvoiceService(
    IInvoiceRepository invoices,
    ICreditNoteRepository creditNotes,
    IInvoicePdfRenderer renderer,
    InvoiceSeller seller)
{
    /// <summary>PDF faktury k objednávce, nebo null (faktura zatím není, nebo patří někomu jinému – Web vrátí 404).</summary>
    public async Task<InvoicePdf?> GetPdfAsync(int orderId, string? customerId, CancellationToken cancellationToken = default)
    {
        var invoice = await invoices.GetByOrderAsync(orderId, cancellationToken);
        if (invoice is null || !IsAllowed(invoice.CustomerId, customerId))
        {
            return null;
        }

        // PDF se skládá vždy znovu ze snímku faktury – údaje se nemění, jen vzhled dokumentu.
        return new InvoicePdf($"faktura-{invoice.Number}.pdf", renderer.Render(invoice, seller));
    }

    /// <summary>E-shop krok 15B: PDF dobropisu k objednávce, nebo null (není, nebo je cizí).</summary>
    public async Task<InvoicePdf?> GetCreditNotePdfAsync(int orderId, string? customerId, CancellationToken cancellationToken = default)
    {
        var creditNote = await creditNotes.GetByOrderAsync(orderId, cancellationToken);
        if (creditNote is null || !IsAllowed(creditNote.CustomerId, customerId))
        {
            return null;
        }

        return new InvoicePdf($"dobropis-{creditNote.Number}.pdf", renderer.Render(creditNote, seller));
    }

    /// <summary>Čísla faktury a dobropisu k objednávce (odkazy na detailu objednávky).</summary>
    public async Task<OrderDocumentsDto> GetDocumentsAsync(int orderId, string? customerId, CancellationToken cancellationToken = default)
    {
        var invoice = await invoices.GetByOrderAsync(orderId, cancellationToken);
        if (invoice is null || !IsAllowed(invoice.CustomerId, customerId))
        {
            return new OrderDocumentsDto(null, null);
        }

        var creditNote = await creditNotes.GetByOrderAsync(orderId, cancellationToken);
        return new OrderDocumentsDto(invoice.Number, creditNote?.Number);
    }

    public async Task<IReadOnlyList<InvoiceSummaryDto>> GetLatestAsync(int count, CancellationToken cancellationToken = default)
    {
        var latest = await invoices.GetLatestAsync(count, cancellationToken);
        var credited = await creditNotes.GetByOrdersAsync(latest.Select(invoice => invoice.OrderId).ToList(), cancellationToken);
        return latest
            .Select(invoice => new InvoiceSummaryDto(invoice.Number, invoice.OrderId, invoice.CustomerName, invoice.Total,
                invoice.IssuedAtUtc, credited.GetValueOrDefault(invoice.OrderId)?.Number))
            .ToList();
    }

    private static bool IsAllowed(string ownerId, string? customerId) => customerId is null || ownerId == customerId;
}
