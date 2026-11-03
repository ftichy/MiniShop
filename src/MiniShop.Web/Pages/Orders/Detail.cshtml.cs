using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MiniShop.Application.Exceptions;
using MiniShop.Application.Ordering;
using MiniShop.Web.Accounts;

namespace MiniShop.Web.Pages.Orders;

/// <summary>
/// E-shop krok 5: detail objednávky – položky, doprava, adresa a časová osa stavů („kde je moje objednávka“).
/// </summary>
/// <remarks>
/// Jen přihlášený (fallback policy). Zákazník vidí jen svou objednávku – cizí se tváří jako neexistující (404),
/// admin jakoukoli. Zrušení a vyřízení posílají formuláře na stránku Objednávky (tam jsou handlery i kontrola práv).
/// </remarks>
public sealed class DetailModel(IOrderService orderService, IAuthorizationService authorization) : PageModel
{
    public OrderDetailDto Detail { get; private set; } = null!;

    public OrderDto Order => Detail.Order;

    /// <summary>Admin: vyřízení a odkazy na průběh zadání (Journeys).</summary>
    public bool CanManageOrders { get; private set; }

    /// <summary>Hláška z pokladny („Objednávka č. 5 přijata…“) – pokladna sem po odeslání přesměruje.</summary>
    [TempData]
    public string? StatusMessage { get; set; }

    public async Task<IActionResult> OnGetAsync(int id, CancellationToken cancellationToken)
    {
        try
        {
            Detail = await orderService.GetDetailAsync(id, User.ToOrderAccess(), cancellationToken);
        }
        catch (NotFoundException)
        {
            return NotFound();
        }

        CanManageOrders = (await authorization.AuthorizeAsync(User, AuthorizationPolicies.RequireAdmin)).Succeeded;
        return Page();
    }

    /// <summary>Čas přechodu pro člověka; starší objednávky (před e-shop krokem 5) ho u pozdějších stavů nemají.</summary>
    public static string FormatTime(DateTime? changedAtUtc) =>
        changedAtUtc is { } time ? time.ToLocalTime().ToString("g") : "čas neznámý (starší objednávka)";

    /// <summary>Popis kroku na časové ose – co se s objednávkou stalo, ne jen název stavu.</summary>
    public static string HistoryText(string status) => status switch
    {
        "Pending" => "Objednávka přijata, ověřujeme sklad",
        "Confirmed" => "Sklad zboží zarezervoval – objednávka potvrzena",
        "Rejected" => "Objednávka zamítnuta",
        "Completed" => "Objednávka vyřízena",
        "Cancelled" => "Objednávka zrušena",
        _ => status
    };
}
