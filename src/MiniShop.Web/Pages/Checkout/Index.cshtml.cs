using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MiniShop.Application.Exceptions;
using MiniShop.Application.Ordering.Checkout;
using MiniShop.Domain.Exceptions;
using MiniShop.Domain.Orders;
using MiniShop.Web.Accounts;

namespace MiniShop.Web.Pages.Checkout;

/// <summary>
/// E-shop krok 4: pokladna – souhrn košíku, doprava, adresa a odeslání objednávky.
/// </summary>
/// <remarks>
/// Formulář nese <see cref="CheckoutRequest.CheckoutId"/> (verzi košíku): dvojklik nebo F5 po odeslání vrátí stejnou
/// objednávku a starý formulář po změně košíku v jiné záložce neprojde. Pravidla hlídá doména, stránka jen ukazuje hlášky.
/// </remarks>
[Authorize(Policy = AuthorizationPolicies.RequireCustomer)]
public sealed class IndexModel(ICheckoutService checkout) : PageModel
{
    public CheckoutDto Summary { get; private set; } = null!;

    [BindProperty]
    public CheckoutRequest Input { get; set; } = new();

    /// <summary>Hláška pro stránku Objednávky (stejný klíč TempData jako její <c>StatusMessage</c>).</summary>
    [TempData]
    public string? StatusMessage { get; set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        await LoadAsync(cancellationToken);
        if (Summary.Cart.IsEmpty)
        {
            return RedirectToPage("/Cart/Index");
        }

        // Předvyplnit z poslední objednávky kurýrem – kdo jednou dal adresu, nejspíš chce znovu stejnou.
        Input.ShippingMethod = Summary.LastAddress is null ? ShippingMethod.Pickup : ShippingMethod.Courier;
        Input.Street = Summary.LastAddress?.Street;
        Input.City = Summary.LastAddress?.City;
        Input.PostalCode = Summary.LastAddress?.PostalCode;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (ModelState.IsValid)
        {
            try
            {
                var order = await checkout.PlaceOrderAsync(Input, User.ToOrderCustomer(), cancellationToken);
                StatusMessage = $"Objednávka č. {order.Id} přijata (celkem {order.TotalPrice:N2} Kč), ověřujeme sklad…";
                return RedirectToPage("/Orders/Index");
            }
            catch (Exception ex) when (ex is DomainException or ConcurrencyConflictException)
            {
                ModelState.AddModelError(string.Empty, ex.Message);
            }
        }

        // Stránka znovu s aktuálním košíkem: zadaná adresa zůstane, verze košíku se obnoví – zákazník vidí,
        // co se změnilo, a další odeslání už platí pro to, co má před sebou.
        await LoadAsync(cancellationToken);
        ModelState.Remove($"{nameof(Input)}.{nameof(CheckoutRequest.CheckoutId)}");
        Input.CheckoutId = Summary.CheckoutId;
        return Page();
    }

    public async Task<IActionResult> OnPostAcceptPricesAsync(CancellationToken cancellationToken)
    {
        await checkout.AcceptCurrentPricesAsync(User.ToCustomerId(), cancellationToken);
        return RedirectToPage();
    }

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        Summary = await checkout.GetAsync(User.ToCustomerId(), cancellationToken);
        Input.CheckoutId = Input.CheckoutId == Guid.Empty ? Summary.CheckoutId : Input.CheckoutId;
    }

    public static string ShippingName(ShippingMethod method) => method switch
    {
        ShippingMethod.Pickup => "Osobní odběr na prodejně",
        ShippingMethod.Courier => "Kurýr na adresu",
        _ => "Nezadáno"
    };
}
