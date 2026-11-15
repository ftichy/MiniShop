using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MiniShop.Application.Abstractions;
using MiniShop.Application.Exceptions;
using MiniShop.Application.Reviews;
using MiniShop.Web.Accounts;
using MiniShop.Web.Ui;

namespace MiniShop.Web.Pages.Admin.Reviews;

/// <summary>
/// E-shop krok 14: moderace hodnocení – nejnovější hodnocení a tlačítko Skrýt/Zobrazit. Skrytí přepočítá průměr
/// a pošle nový souhrn (ProductRatingChangedV1) do výlohy.
/// </summary>
[Authorize(Policy = AuthorizationPolicies.RequireAdmin)]
public sealed class IndexModel(ReviewService reviews) : PageModel
{
    public PagedList<ReviewAdminDto> ReviewPage { get; private set; } = new([], 1, PageRequest.DefaultSize, 0);
    public IReadOnlyList<ReviewAdminDto> Reviews => ReviewPage.Items;
    public PagerModel Pager => PagerModel.From(ReviewPage, "reviews");

    [BindProperty(SupportsGet = true, Name = PagerModel.PageParameter)]
    public int? PageNumber { get; set; }

    [BindProperty(SupportsGet = true, Name = PagerModel.SizeParameter)]
    public int? PageSize { get; set; }

    [TempData]
    public string? StatusMessage { get; set; }

    public async Task OnGetAsync(CancellationToken cancellationToken) =>
        ReviewPage = await reviews.GetPageAsync(PageRequest.Of(PageNumber, PageSize), cancellationToken);

    public async Task<IActionResult> OnPostToggleAsync(int id, bool hide, CancellationToken cancellationToken)
    {
        try
        {
            await reviews.SetHiddenAsync(id, hide, cancellationToken);
            StatusMessage = hide ? "Hodnocení skryto – průměr ve výloze se za chvilku přepočítá." : "Hodnocení je zase vidět.";
        }
        catch (NotFoundException)
        {
            StatusMessage = "Hodnocení neexistuje.";
        }

        return RedirectToPage(new { p = PageNumber, size = PageSize });   // zpět na stejnou stránku seznamu
    }
}
