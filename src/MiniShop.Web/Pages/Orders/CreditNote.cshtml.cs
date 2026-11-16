using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MiniShop.Application.Invoicing;

namespace MiniShop.Web.Pages.Orders;

/// <summary>
/// E-shop krok 15B: PDF dobropisu ke stažení (<c>/Orders/{id}/CreditNote</c>). Stejná pravidla jako faktura:
/// zákazník jen ke své objednávce, admin ke každé; cizí nebo neexistující dobropis = 404.
/// E-shop krok 22: k objednávce může být dobropisů víc – <c>/Orders/{id}/CreditNote/{číslo}</c>, bez čísla poslední.
/// </summary>
public sealed class CreditNoteModel(InvoiceService invoices, IAuthorizationService authorization) : PageModel
{
    public async Task<IActionResult> OnGetAsync(int id, string? number, CancellationToken cancellationToken)
    {
        var customerId = await InvoiceAccess.CustomerIdOrAdminAsync(User, authorization);
        var pdf = await invoices.GetCreditNotePdfAsync(id, customerId, number, cancellationToken);
        return pdf is null ? NotFound() : File(pdf.Content, InvoicePdf.ContentType, pdf.FileName);
    }
}
