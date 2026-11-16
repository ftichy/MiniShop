namespace MiniShop.Application.Invoicing;

/// <summary>
/// E-shop krok 15: čtení faktur – PDF ke stažení (zákazník jen svou, admin jakoukoli) a přehled pro admina.
/// E-shop krok 15B: totéž pro dobropisy (od kroku 22 jich k objednávce může být víc). Kdo smí co, rozhoduje volající (Web)
/// parametrem <c>customerId</c>: null = admin.
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

    /// <summary>
    /// E-shop krok 15B: PDF dobropisu k objednávce, nebo null (není, nebo je cizí). Krok 22: <paramref name="number"/> vybere
    /// konkrétní dobropis; bez čísla = poslední vystavený (odkazy ze starších e-mailů a stránek).
    /// </summary>
    public async Task<InvoicePdf?> GetCreditNotePdfAsync(
        int orderId, string? customerId, string? number = null, CancellationToken cancellationToken = default)
    {
        var creditNote = number is null
            ? (await creditNotes.GetForOrderAsync(orderId, cancellationToken)).LastOrDefault()
            : await creditNotes.GetByNumberAsync(number, cancellationToken);
        if (creditNote is null || creditNote.OrderId != orderId || !IsAllowed(creditNote.CustomerId, customerId))
        {
            return null;
        }

        return new InvoicePdf($"dobropis-{creditNote.Number}.pdf", renderer.Render(creditNote, seller));
    }

    /// <summary>Číslo faktury a dobropisy k objednávce (odkazy na detailu objednávky).</summary>
    public async Task<OrderDocumentsDto> GetDocumentsAsync(int orderId, string? customerId, CancellationToken cancellationToken = default)
    {
        var invoice = await invoices.GetByOrderAsync(orderId, cancellationToken);
        if (invoice is null || !IsAllowed(invoice.CustomerId, customerId))
        {
            return new OrderDocumentsDto(null, []);
        }

        var credited = await creditNotes.GetForOrderAsync(orderId, cancellationToken);
        return new OrderDocumentsDto(invoice.Number,
            credited.Select(note => new CreditNoteSummaryDto(note.Number, note.Total, note.ReturnNumber)).ToList());
    }

    /// <summary>Stránka faktur pro přehled admina (nejnovější první) – dobropisy se dohledají jen k fakturám na stránce.</summary>
    public async Task<MiniShop.Application.Abstractions.PagedList<InvoiceSummaryDto>> GetPageAsync(
        MiniShop.Application.Abstractions.PageRequest page, CancellationToken cancellationToken = default)
    {
        var invoicePage = await invoices.GetPageAsync(page, cancellationToken);
        var credited = await creditNotes.GetByOrdersAsync(invoicePage.Items.Select(invoice => invoice.OrderId).ToList(), cancellationToken);
        return invoicePage.Map(invoice => new InvoiceSummaryDto(invoice.Number, invoice.OrderId, invoice.CustomerName,
            invoice.Total, invoice.IssuedAtUtc,
            credited.TryGetValue(invoice.OrderId, out var notes) ? notes.Select(note => note.Number).ToList() : []));
    }

    private static bool IsAllowed(string ownerId, string? customerId) => customerId is null || ownerId == customerId;
}
