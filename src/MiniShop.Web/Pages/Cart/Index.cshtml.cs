using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MiniShop.Application.Exceptions;
using MiniShop.Application.Ordering.Carts;
using MiniShop.Domain.Exceptions;
using MiniShop.Web.Accounts;

namespace MiniShop.Web.Pages.Cart;

/// <summary>
/// E-shop krok 3: košík zákazníka – změna množství, odebrání, orientační součet. Přidává se z výlohy (handler Add).
/// </summary>
/// <remarks>
/// Jen pro zákazníka s potvrzeným e-mailem – stejně jako objednávání (admin neobjednává, košík nepotřebuje).
/// Každá akce končí přesměrováním (POST → redirect → GET), hláška putuje přes TempData.
/// </remarks>
[Authorize(Policy = AuthorizationPolicies.RequireCustomer)]
public sealed class IndexModel(ICartService carts, CartHistoryService history) : PageModel
{
    public CartDto Cart { get; private set; } = CartDto.Empty;

    /// <summary>E-shop krok 17: poslední změny košíku (nejnovější první) – z událostí košíku.</summary>
    public IReadOnlyList<CartHistoryEntryDto> History { get; private set; } = [];

    [TempData]
    public string? StatusMessage { get; set; }

    [TempData]
    public string? ErrorMessage { get; set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Cart = await carts.GetAsync(User.ToCustomerId(), cancellationToken);
        History = await history.GetHistoryAsync(User.ToCustomerId(), cancellationToken);
    }

    /// <param name="returnUrl">Kam se vrátit (stránka výlohy i s filtry). Jen lokální adresa – jinak košík.</param>
    public Task<IActionResult> OnPostAddAsync(int productId, string? returnUrl, CancellationToken cancellationToken, int quantity = 1) =>
        ExecuteAsync(async () =>
        {
            var name = await carts.AddItemAsync(User.ToCustomerId(), productId, quantity, cancellationToken);
            StatusMessage = $"„{name}“ je v košíku.";
        }, Url.IsLocalUrl(returnUrl) ? returnUrl : null);

    public Task<IActionResult> OnPostChangeQuantityAsync(int productId, int quantity, CancellationToken cancellationToken) =>
        ExecuteAsync(() => carts.ChangeQuantityAsync(User.ToCustomerId(), productId, quantity, cancellationToken));

    public Task<IActionResult> OnPostRemoveAsync(int productId, CancellationToken cancellationToken) =>
        ExecuteAsync(() => carts.RemoveItemAsync(User.ToCustomerId(), productId, cancellationToken));

    // Porušené pravidlo košíku (vyprodáno, víc než 100 kusů…) není chyba aplikace – jen hláška pro zákazníka.
    private async Task<IActionResult> ExecuteAsync(Func<Task> action, string? returnUrl = null)
    {
        try
        {
            await action();
        }
        catch (DomainException ex)
        {
            ErrorMessage = ex.Message;
        }
        catch (ConcurrencyConflictException)
        {
            // E-shop krok 4: košík mezitím změnila jiná záložka (verze košíku = concurrency token).
            ErrorMessage = "Košík se mezitím změnil v jiném okně. Zkontrolujte ho a zkuste to znovu.";
        }

        return returnUrl is null ? RedirectToPage() : LocalRedirect(returnUrl);
    }
}
