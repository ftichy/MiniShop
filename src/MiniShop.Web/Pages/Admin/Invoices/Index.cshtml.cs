using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MiniShop.Application.Invoicing;
using MiniShop.Web.Accounts;

namespace MiniShop.Web.Pages.Admin.Invoices;

/// <summary>E-shop krok 15: přehled vystavených faktur (nejnovější nahoře) s odkazem na PDF a objednávku.</summary>
[Authorize(Policy = AuthorizationPolicies.RequireAdmin)]
public sealed class IndexModel(InvoiceService invoices) : PageModel
{
    private const int Count = 100;

    public IReadOnlyList<InvoiceSummaryDto> Invoices { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken cancellationToken) =>
        Invoices = await invoices.GetLatestAsync(Count, cancellationToken);
}
