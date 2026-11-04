using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MiniShop.Application.Exceptions;
using MiniShop.Application.Reviews;
using MiniShop.Web.Accounts;

namespace MiniShop.Web.Pages.Admin.Reviews;

/// <summary>
/// E-shop krok 14: moderace hodnocení – nejnovější hodnocení a tlačítko Skrýt/Zobrazit. Skrytí přepočítá průměr
/// a pošle nový souhrn (ProductRatingChangedV1) do výlohy.
/// </summary>
[Authorize(Policy = AuthorizationPolicies.RequireAdmin)]
public sealed class IndexModel(ReviewService reviews) : PageModel
{
    public IReadOnlyList<ReviewAdminDto> Reviews { get; private set; } = [];

    [TempData]
    public string? StatusMessage { get; set; }

    public async Task OnGetAsync(CancellationToken cancellationToken) =>
        Reviews = await reviews.GetLatestAsync(cancellationToken: cancellationToken);

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

        return RedirectToPage();
    }
}
