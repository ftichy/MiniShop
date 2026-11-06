using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MiniShop.Application.Invoicing;
using MiniShop.Web.Accounts;

namespace MiniShop.Web.Pages.Orders;

/// <summary>
/// E-shop krok 15: PDF faktury ke stažení (<c>/Orders/{id}/Invoice</c>). Zákazník jen ke své objednávce, admin ke
/// každé; cizí nebo zatím nevystavená faktura = 404 (neprozradí, že existuje).
/// </summary>
public sealed class InvoiceModel(InvoiceService invoices, IAuthorizationService authorization) : PageModel
{
    public async Task<IActionResult> OnGetAsync(int id, CancellationToken cancellationToken)
    {
        var customerId = await InvoiceAccess.CustomerIdOrAdminAsync(User, authorization);
        var pdf = await invoices.GetPdfAsync(id, customerId, cancellationToken);
        return pdf is null ? NotFound() : File(pdf.Content, InvoicePdf.ContentType, pdf.FileName);
    }
}

/// <summary>E-shop krok 15: komu patří faktury – admin vidí všechny (null), zákazník jen své (jeho ID).</summary>
public static class InvoiceAccess
{
    public static async Task<string?> CustomerIdOrAdminAsync(ClaimsPrincipal user, IAuthorizationService authorization) =>
        (await authorization.AuthorizeAsync(user, AuthorizationPolicies.RequireAdmin)).Succeeded
            ? null
            : user.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
}
