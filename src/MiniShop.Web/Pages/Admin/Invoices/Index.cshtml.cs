using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MiniShop.Application.Abstractions;
using MiniShop.Application.Invoicing;
using MiniShop.Web.Accounts;
using MiniShop.Web.Ui;

namespace MiniShop.Web.Pages.Admin.Invoices;

/// <summary>E-shop krok 15: přehled vystavených faktur (nejnovější nahoře) s odkazem na PDF a objednávku – po stránkách.</summary>
[Authorize(Policy = AuthorizationPolicies.RequireInvoices)]   // účty krok 12B
public sealed class IndexModel(InvoiceService invoices) : PageModel
{
    public PagedList<InvoiceSummaryDto> InvoicePage { get; private set; } = new([], 1, PageRequest.DefaultSize, 0);
    public IReadOnlyList<InvoiceSummaryDto> Invoices => InvoicePage.Items;
    public PagerModel Pager => PagerModel.From(InvoicePage, "invoices");

    [BindProperty(SupportsGet = true, Name = PagerModel.PageParameter)]
    public int? PageNumber { get; set; }

    [BindProperty(SupportsGet = true, Name = PagerModel.SizeParameter)]
    public int? PageSize { get; set; }

    public async Task OnGetAsync(CancellationToken cancellationToken) =>
        InvoicePage = await invoices.GetPageAsync(PageRequest.Of(PageNumber, PageSize), cancellationToken);
}
