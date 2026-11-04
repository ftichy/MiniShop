using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MiniShop.Application.Ordering;
using MiniShop.Application.Ordering.Catalog;
using MiniShop.Application.Reviews;
using MiniShop.Domain.Exceptions;
using MiniShop.Domain.Reviews;
using MiniShop.Web.Accounts;

namespace MiniShop.Web.Pages.Shop;

/// <summary>
/// E-shop krok 14: detail produktu – údaje z kopie katalogu (Ordering) a hodnocení z modulu Reviews. Moduly se
/// neznají, spojí je až tahle stránka (jako smazání účtu). Veřejná; hodnotit smí jen zákazník, který produkt dostal.
/// </summary>
[AllowAnonymous]
public sealed class DetailModel(ICatalogQueryService catalog, ReviewService reviews, IAuthorizationService authorization)
    : PageModel
{
    public CatalogItemDto Product { get; private set; } = null!;

    public ProductReviewsDto Reviews { get; private set; } = null!;

    public bool CanOrder { get; private set; }

    public bool IsAnonymous => User.Identity?.IsAuthenticated != true;

    public string CurrentUrl => Request.Path + Request.QueryString;

    [TempData]
    public string? StatusMessage { get; set; }

    [TempData]
    public string? ErrorMessage { get; set; }

    public async Task<IActionResult> OnGetAsync(int id, CancellationToken cancellationToken) =>
        await LoadAsync(id, cancellationToken) ? Page() : NotFound();

    /// <summary>Nové hodnocení nebo úprava vlastního. Jen zákazník; „koupil a dostal“ hlídá ReviewService.</summary>
    public async Task<IActionResult> OnPostReviewAsync(int id, int rating, string? text, CancellationToken cancellationToken)
    {
        if (IsAnonymous)
        {
            return Challenge();   // stránka je veřejná, hodnocení ne – nejdřív přihlásit
        }

        if (!(await authorization.AuthorizeAsync(User, AuthorizationPolicies.RequireCustomer)).Succeeded)
        {
            return Forbid();
        }

        try
        {
            await reviews.SubmitAsync(id, User.ToCustomerId(), rating, text, cancellationToken);
            StatusMessage = "Děkujeme za hodnocení! Ve výloze se projeví za pár sekund.";
        }
        catch (DomainException ex)
        {
            ErrorMessage = ex.Message;
        }

        return RedirectToPage(new { id });
    }

    private async Task<bool> LoadAsync(int id, CancellationToken cancellationToken)
    {
        if (await catalog.GetProductAsync(id, cancellationToken) is not { } product)
        {
            return false;
        }

        Product = product;
        CanOrder = (await authorization.AuthorizeAsync(User, AuthorizationPolicies.RequireCustomer)).Succeeded;
        Reviews = await reviews.GetForProductAsync(id, CanOrder ? User.ToCustomerId() : null, cancellationToken);
        return true;
    }

    public static int MaxTextLength => Review.TextMaxLength;
}
