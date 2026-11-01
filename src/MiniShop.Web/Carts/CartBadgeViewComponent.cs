using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MiniShop.Application.Ordering.Carts;
using MiniShop.Web.Accounts;

namespace MiniShop.Web.Carts;

/// <summary>
/// E-shop krok 3: odkaz „Košík (počet kusů)“ v hlavičce. Jen pro zákazníka, který smí objednávat –
/// ostatním se nic nevykreslí. Na každé stránce jeden malý dotaz (SUM v SQL).
/// </summary>
public sealed class CartBadgeViewComponent(ICartService carts, IAuthorizationService authorization) : ViewComponent
{
    public async Task<IViewComponentResult> InvokeAsync()
    {
        if (!(await authorization.AuthorizeAsync(UserClaimsPrincipal, AuthorizationPolicies.RequireCustomer)).Succeeded)
        {
            return Content(string.Empty);
        }

        return View(await carts.GetTotalQuantityAsync(UserClaimsPrincipal.ToCustomerId(), HttpContext.RequestAborted));
    }
}
