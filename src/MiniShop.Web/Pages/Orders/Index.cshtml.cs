using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MiniShop.Application.Ordering;
using MiniShop.Application.Ordering.Catalog;
using MiniShop.Application.Exceptions;
using MiniShop.Domain.Exceptions;
using MiniShop.Infrastructure.Accounts;
using MiniShop.Web.Accounts;

namespace MiniShop.Web.Pages.Orders;

/// <summary>
/// Moje objednávky (zákazník) / všechny objednávky (admin): stav, zrušení, vyřízení.
/// </summary>
/// <remarks>
/// E-shop krok 4B: formulář „Nová objednávka“ (jeden produkt) odtud zmizel – objednává se jen přes košík a pokladnu
/// (<c>/Checkout</c>), kam se přesunulo i sledování průběhu (Journeys).
/// </remarks>
public sealed class IndexModel(
    IOrderService orderService,
    CatalogSyncService catalogSync,
    IAuthorizationService authorization) : PageModel
{
    /// <summary>Zákazník, který smí objednávat – vidí odkaz do obchodu.</summary>
    public bool CanPlaceOrders { get; private set; }

    /// <summary>Účty krok 9: zákazník, který ještě nepotvrdil e-mail – výzva k potvrzení.</summary>
    public bool NeedsEmailConfirmation { get; private set; }

    /// <summary>Účty krok 2: vyřízení, obnova katalogu a průběh objednávek jen pro admina.</summary>
    public bool CanManageOrders { get; private set; }

    public IReadOnlyList<OrderDto> Orders { get; private set; } = [];

    [TempData]
    public string? StatusMessage { get; set; }

    public Task OnGetAsync(CancellationToken cancellationToken) => LoadAsync(cancellationToken);

    /// <summary>Ruční obnova kopie katalogu – požádá sklad o všechny produkty (SyncCatalogV1).</summary>
    public async Task<IActionResult> OnPostSyncCatalogAsync(CancellationToken cancellationToken)
    {
        if (!await IsAuthorizedAsync(AuthorizationPolicies.RequireAdmin))
        {
            return Forbid();
        }

        await catalogSync.RequestAsync(cancellationToken);
        StatusMessage = "Požádali jsme sklad o celý katalog, produkty se doplní během pár sekund.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostCompleteAsync(int id, CancellationToken cancellationToken)
    {
        if (!await IsAuthorizedAsync(AuthorizationPolicies.RequireAdmin))
        {
            return Forbid();
        }

        return await ExecuteAsync(async () =>
        {
            await orderService.CompleteAsync(id, cancellationToken);
            StatusMessage = $"Objednávka č. {id} byla vyřízena.";
        }, cancellationToken);
    }

    /// <summary>Zákazník ruší jen své objednávky (účty krok 3 – cizí = 404), admin kteroukoli.</summary>
    public Task<IActionResult> OnPostCancelAsync(int id, CancellationToken cancellationToken) =>
        ExecuteAsync(async () =>
        {
            await orderService.CancelAsync(id, User.ToOrderAccess(), cancellationToken);
            StatusMessage = $"Objednávka č. {id} byla zrušena, zboží se za chvilku vrátí na sklad.";
        }, cancellationToken);

    private async Task<IActionResult> ExecuteAsync(Func<Task> action, CancellationToken cancellationToken)
    {
        try
        {
            await action();
            return RedirectToPage();
        }
        catch (Exception ex) when (ex is DomainException or NotFoundException)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            await LoadAsync(cancellationToken);
            return Page();
        }
    }

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        CanPlaceOrders = await IsAuthorizedAsync(AuthorizationPolicies.RequireCustomer);
        NeedsEmailConfirmation = !CanPlaceOrders && User.IsInRole(AccountRoles.User);
        CanManageOrders = await IsAuthorizedAsync(AuthorizationPolicies.RequireAdmin);
        Orders = await orderService.GetAllAsync(User.ToOrderAccess(), cancellationToken);
    }

    /// <summary>
    /// Účty krok 2: Razor Pages umí [Authorize] jen na celou stránku, tady má ale každý handler jiné pravidlo.
    /// Proto kontrola v kódu – se stejnými policies, aby pravidla zůstala na jednom místě.
    /// </summary>
    private async Task<bool> IsAuthorizedAsync(string policy) =>
        (await authorization.AuthorizeAsync(User, policy)).Succeeded;

    /// <summary>Krok 9: dokud nějaká objednávka čeká na sklad, stránka se sama obnovuje (jednoduchý polling).</summary>
    public bool HasPendingOrders => Orders.Any(o => o.Status == "Pending");

    public static string StatusBadge(string status) => status switch
    {
        "Pending" => "bg-warning text-dark",
        "Confirmed" => "bg-primary",
        "Rejected" => "bg-danger",
        "Completed" => "bg-success",
        _ => "bg-secondary"
    };

    public static string StatusText(string status) => status switch
    {
        "Pending" => "Ověřujeme sklad…",
        "Confirmed" => "Potvrzená",
        "Rejected" => "Zamítnutá",
        "Completed" => "Vyřízená",
        "Cancelled" => "Zrušená",
        _ => status
    };
}
