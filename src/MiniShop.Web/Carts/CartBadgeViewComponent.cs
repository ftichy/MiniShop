using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MiniShop.Application.Ordering.Carts;
using MiniShop.Web.Accounts;

namespace MiniShop.Web.Carts;

/// <summary>
/// E-shop krok 3: odkaz „Košík (počet kusů)“ v hlavičce. Jen pro zákazníka, který smí objednávat –
/// ostatním se nic nevykreslí. Na každé stránce jeden malý dotaz (SUM v SQL).
/// Vzhled krok D: <paramref name="variant"/> „tab“ = položka spodní lišty na mobilu (pohled <c>Tab</c>); počet kusů se
/// pro obě místa načte jen jednou za požadavek (<see cref="HttpContext.Items"/>).
/// </summary>
public sealed class CartBadgeViewComponent(ICartService carts, IAuthorizationService authorization) : ViewComponent
{
    public const string TabVariant = "tab";
    private const string CountKey = "minishop:cart-badge-count";

    public async Task<IViewComponentResult> InvokeAsync(string? variant = null)
    {
        if (!(await authorization.AuthorizeAsync(UserClaimsPrincipal, AuthorizationPolicies.RequireCustomer)).Succeeded)
        {
            return Content(string.Empty);
        }

        if (HttpContext.Items[CountKey] is not int count)
        {
            count = await carts.GetTotalQuantityAsync(UserClaimsPrincipal.ToCustomerId(), HttpContext.RequestAborted);
            HttpContext.Items[CountKey] = count;
        }

        return variant == TabVariant ? View("Tab", count) : View(count);
    }
}
